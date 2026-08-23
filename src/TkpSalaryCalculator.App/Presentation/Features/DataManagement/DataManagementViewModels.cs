using TkpSalaryCalculator.App.Navigation;
using TkpSalaryCalculator.App.Presentation.Common;
using TkpSalaryCalculator.App.Presentation.Services;
using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.Ports;
using TkpSalaryCalculator.Application.UseCases;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.App.Presentation.Features.DataManagement;

/// <summary>SCR-DATA-01 のストリーム入出力、検証確認、および全置換後の再起動を管理します。</summary>
public sealed class DataManagementViewModel : ViewModelBase
{
    private readonly IDataTransferUseCase transfers;
    private readonly IBackupReminderUseCase backupReminder;
    private readonly IPlatformDocumentService documents;
    private readonly IAppInformationService appInformation;
    private readonly IConfirmationDialogService dialogs;
    private readonly IUserNotificationService notifications;
    private readonly IAppRootNavigator rootNavigator;
    private readonly IAppSessionState sessionState;
    private readonly IUtcClock clock;
    private readonly ILocalDateConverter localDates;
    private string lastExportText = "まだエクスポートしていません。";
    private string successMessage = string.Empty;

    public DataManagementViewModel(
        IDataTransferUseCase transfers,
        IBackupReminderUseCase backupReminder,
        IPlatformDocumentService documents,
        IAppInformationService appInformation,
        IConfirmationDialogService dialogs,
        IUserNotificationService notifications,
        IAppRootNavigator rootNavigator,
        IAppSessionState sessionState,
        IUtcClock clock,
        ILocalDateConverter localDates,
        IUserErrorPresenter errorPresenter) : base(errorPresenter)
    {
        this.transfers = transfers ?? throw new ArgumentNullException(nameof(transfers));
        this.backupReminder = backupReminder ?? throw new ArgumentNullException(nameof(backupReminder));
        this.documents = documents ?? throw new ArgumentNullException(nameof(documents));
        this.appInformation = appInformation ?? throw new ArgumentNullException(nameof(appInformation));
        this.dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        this.notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        this.rootNavigator = rootNavigator ?? throw new ArgumentNullException(nameof(rootNavigator));
        this.sessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.localDates = localDates ?? throw new ArgumentNullException(nameof(localDates));
        TrackDataChanges(sessionState, AppDataChangeKind.BackupStatus);
        ExportCommand = new AsyncCommand(ExportAsync, PresentError);
        ImportCommand = new AsyncCommand(ImportAsync, PresentError);
    }

    public string LastExportText { get => lastExportText; private set => SetProperty(ref lastExportText, value); }

    public string SuccessMessage
    {
        get => successMessage;
        private set
        {
            if (!SetProperty(ref successMessage, value)) return;
            OnPropertyChanged(nameof(HasSuccessMessage));
        }
    }

    public bool HasSuccessMessage => !string.IsNullOrWhiteSpace(SuccessMessage);

    public AsyncCommand ExportCommand { get; }

    public AsyncCommand ImportCommand { get; }

    public Task LoadAsync() => LoadTrackedAsync(LoadCoreAsync, force: true);

    public Task LoadIfNeededAsync() => LoadTrackedAsync(LoadCoreAsync, force: false);

    public Task ExportAsync() => RunBusyAsync(async cancellationToken =>
    {
        SuccessMessage = string.Empty;
        var confirmed = await dialogs.ConfirmAsync(
            "データをエクスポート",
            "出力ファイルは暗号化・パスワード保護されません。第三者が閲覧できる場所を避け、保存先を確認してください。",
            "保存先を選ぶ", "キャンセル", cancellationToken);
        if (!confirmed) return;

        var fileName = $"tkp-salary-{localDates.ToLocalDate(clock.UtcNow):yyyyMMdd}.tkpsalary";
        await using var destination = await documents.CreateExportAsync(fileName, cancellationToken);
        if (destination is null) return;
        await transfers.ExportAsync(destination, appInformation.DisplayVersion, cancellationToken);
        sessionState.NotifyDataChanged(AppDataChangeKind.BackupStatus);
        var generation = CaptureTrackedDataGeneration();
        await LoadCoreAsync(cancellationToken);
        AcceptDataGeneration(generation);
        SuccessMessage = "データをエクスポートしました。";
    });

    public Task ImportAsync() => RunBusyAsync(async cancellationToken =>
    {
        SuccessMessage = string.Empty;
        await using var source = await documents.OpenImportAsync(cancellationToken);
        if (source is null) return;

        ImportPreviewDto? preview = null;
        var committed = false;
        try
        {
            preview = await transfers.PrepareImportAsync(source, cancellationToken);
            var confirmed = await dialogs.ConfirmAsync(
                "データをすべて置き換えますか",
                BuildImportMessage(preview),
                "置き換える", "キャンセル", cancellationToken);
            if (!confirmed) return;

            await transfers.CommitImportAsync(preview.Id, cancellationToken);
            committed = true;
            ResetSession();
            await rootNavigator.SetRootAsync(new AppRootNavigationRequest(AppRootKind.Main, null), cancellationToken);
            await notifications.ShowAsync(
                "インポート完了",
                "データを置き換え、すべての画面を最新の内容で読み直しました。",
                CancellationToken.None);
        }
        finally
        {
            if (preview is not null && !committed)
            {
                try { await transfers.DiscardImportAsync(preview.Id, CancellationToken.None); }
                catch { /* 次回の準備処理でも清掃されるため、元の取消・失敗を優先する。 */ }
            }
        }
    });

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var state = await backupReminder.GetStateAsync(localDates.ToLocalDate(clock.UtcNow), cancellationToken);
        LastExportText = state.LastExportedAtUtc is { } value
            ? $"最終エクスポート: {value.ToLocalTime():yyyy年M月d日 H:mm}"
            : "まだエクスポートしていません。";
    }

    private void ResetSession()
    {
        var today = localDates.ToLocalDate(clock.UtcNow);
        sessionState.InitialSetupState = null;
        sessionState.SelectedRootRoute = NavigationRoutes.Home;
        sessionState.CalendarMonth = new YearMonth(today.Year, today.Month);
        sessionState.SelectedCalendarDate = today;
        sessionState.SettingsMonth = new YearMonth(today.Year, today.Month);
        sessionState.PayrollPeriod = null;
        sessionState.ResetDataGenerations();
    }

    private static string BuildImportMessage(ImportPreviewDto value)
    {
        var settingRange = value.OldestSettingMonth is { } oldestMonth && value.LatestSettingMonth is { } latestMonth
            ? $"設定年月: {oldestMonth.Year:D4}-{oldestMonth.Month:D2} ～ {latestMonth.Year:D4}-{latestMonth.Month:D2}"
            : "設定年月: なし";
        var workRange = value.OldestWorkDate is { } oldestDate && value.LatestWorkDate is { } latestDate
            ? $"勤務日: {oldestDate:yyyy-MM-dd} ～ {latestDate:yyyy-MM-dd}"
            : "勤務日: なし";
        var warnings = value.Warnings.Count == 0
            ? string.Empty
            : $"{Environment.NewLine}警告:{Environment.NewLine}{string.Join(Environment.NewLine, value.Warnings.Select(x => $"・{x.Message}"))}";
        return $"作成日時: {value.ExportCreatedAtUtc.ToLocalTime():yyyy年M月d日 H:mm}{Environment.NewLine}" +
               $"データ形式: v{value.FormatVersion}{Environment.NewLine}" +
               $"設定年月 {value.SettingMonthCount}件 / 基本シフト {value.BasicShiftCount}件 / 勤務記録 {value.WorkRecordCount}件 / 月額手当 {value.MonthlyAllowanceCount}件{Environment.NewLine}" +
               $"{settingRange}{Environment.NewLine}{workRange}{Environment.NewLine}{Environment.NewLine}" +
               $"現在の設定・基本シフト・勤務記録・月額手当はすべて失われ、この内容へ置き換わります。{warnings}";
    }
}

/// <summary>SCR-APP-01 のアプリ情報を表示します。</summary>
public sealed class AppInformationViewModel : ViewModelBase
{
    private readonly IDataTransferUseCase transfers;
    private string dataFormatVersion = "確認中";

    public AppInformationViewModel(
        IDataTransferUseCase transfers,
        IAppInformationService appInformation,
        IUserErrorPresenter errorPresenter) : base(errorPresenter)
    {
        this.transfers = transfers ?? throw new ArgumentNullException(nameof(transfers));
        ArgumentNullException.ThrowIfNull(appInformation);
        AppName = appInformation.Name;
        DisplayVersion = appInformation.DisplayVersion;
        BuildNumber = appInformation.BuildNumber;
    }

    public string AppName { get; }
    public string DisplayVersion { get; }
    public string BuildNumber { get; }
    public string DataFormatVersion { get => dataFormatVersion; private set => SetProperty(ref dataFormatVersion, value); }

    public Task LoadAsync() => RunBusyAsync(async cancellationToken =>
    {
        var format = await transfers.GetFormatAsync(cancellationToken);
        DataFormatVersion = $"{format.Format} / v{format.FormatVersion}";
    });
}
