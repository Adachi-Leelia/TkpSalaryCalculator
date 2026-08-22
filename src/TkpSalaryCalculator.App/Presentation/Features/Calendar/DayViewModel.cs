using TkpSalaryCalculator.App.Presentation.Common;
using TkpSalaryCalculator.App.Presentation.Services;
using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.UseCases;
using TkpSalaryCalculator.Domain.Contracts;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.App.Presentation.Features.Calendar;

/// <summary>SCR-DAY-01 の日別給与と勤務行を管理します。</summary>
public sealed class DayViewModel : ViewModelBase
{
    private readonly ISalaryQueryUseCase salaryQuery;
    private readonly IWorkRecordUseCase workRecords;
    private readonly ICalendarNavigator navigator;
    private readonly IConfirmationDialogService dialogs;
    private readonly JapaneseDisplayFormatter formatter;
    private DateOnly date;
    private string dateText = string.Empty;
    private string totalText = "0円";
    private string uncalculatedText = string.Empty;
    private string successMessage = string.Empty;
    private DateTime copySourceDate;
    private DateTime copySourceMaximumDate;
    private IReadOnlyList<DayWorkRecordRowViewModel> records = [];

    public DayViewModel(
        ISalaryQueryUseCase salaryQuery,
        IWorkRecordUseCase workRecords,
        ICalendarNavigator navigator,
        IConfirmationDialogService dialogs,
        JapaneseDisplayFormatter formatter,
        IUserErrorPresenter errorPresenter) : base(errorPresenter)
    {
        this.salaryQuery = salaryQuery ?? throw new ArgumentNullException(nameof(salaryQuery));
        this.workRecords = workRecords ?? throw new ArgumentNullException(nameof(workRecords));
        this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        this.dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        this.formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        ReloadCommand = new AsyncCommand(LoadAsync, PresentError);
        AddWorkCommand = new AsyncCommand(AddWorkAsync, PresentError);
        CopyDayCommand = new AsyncCommand(CopyDayAsync, PresentError);
    }

    public DateOnly Date => date;
    public string DateText { get => dateText; private set => SetProperty(ref dateText, value); }
    public string TotalText { get => totalText; private set => SetProperty(ref totalText, value); }
    public string UncalculatedText
    {
        get => uncalculatedText;
        private set
        {
            if (!SetProperty(ref uncalculatedText, value)) return;
            OnPropertyChanged(nameof(HasUncalculated));
        }
    }
    public bool HasUncalculated => !string.IsNullOrWhiteSpace(UncalculatedText);
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
    public DateTime CopySourceDate
    {
        get => copySourceDate;
        set => SetProperty(ref copySourceDate, value.Date);
    }
    public DateTime CopySourceMaximumDate { get => copySourceMaximumDate; private set => SetProperty(ref copySourceMaximumDate, value.Date); }
    public IReadOnlyList<DayWorkRecordRowViewModel> Records
    {
        get => records;
        private set
        {
            if (!SetProperty(ref records, value)) return;
            OnPropertyChanged(nameof(HasRecords));
            OnPropertyChanged(nameof(IsEmpty));
        }
    }
    public bool HasRecords => Records.Count != 0;
    public bool IsEmpty => !HasRecords;
    public AsyncCommand ReloadCommand { get; }
    public AsyncCommand AddWorkCommand { get; }
    public AsyncCommand CopyDayCommand { get; }

    public void SetDate(DateOnly value)
    {
        date = value;
        CopySourceMaximumDate = value.AddDays(-1).ToDateTime(TimeOnly.MinValue);
        CopySourceDate = CopySourceMaximumDate;
        OnPropertyChanged(nameof(Date));
    }

    public Task LoadAsync() => RunBusyAsync(LoadCoreAsync);

    public Task AddWorkAsync() => navigator.OpenWorkEditorAsync(Date, null, CancellationToken.None);

    public Task OpenCalculationDetailsAsync(WorkRecordId id) =>
        navigator.OpenCalculationDetailsAsync(Date, id, CancellationToken.None);

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var salaryTask = salaryQuery.GetDayAsync(Date, cancellationToken);
        var recordsTask = workRecords.GetForDateAsync(Date, cancellationToken);
        var optionsTask = workRecords.GetInputOptionsAsync(Date, cancellationToken);
        await Task.WhenAll(salaryTask, recordsTask, optionsTask);

        var daily = await salaryTask;
        var stored = await recordsTask;
        var options = await optionsTask;
        var calculations = daily.Records.ToDictionary(x => x.WorkRecord.Id);
        var serviceNames = options.Settings.Snapshot.Services.ToDictionary(x => x.Id, x => x.DisplayName);
        var categoryNames = options.Settings.Snapshot.TimeCategories.ToDictionary(x => x.Id, x => x.DisplayName);

        DateText = formatter.Date(Date);
        TotalText = formatter.Money(daily.CalculatedSubtotal);
        UncalculatedText = daily.UncalculatedCount == 0 ? string.Empty : $"未計算 {daily.UncalculatedCount}件。金額は推測せず、勤務を開いて不足設定を確認してください。";
        Records = stored.Select(record =>
        {
            calculations.TryGetValue(record.Id, out var salary);
            var service = serviceNames.GetValueOrDefault(record.ServiceId, "サービス");
            var category = record.TimeCategoryId is { } categoryId ? categoryNames.GetValueOrDefault(categoryId) : null;
            var name = string.IsNullOrWhiteSpace(category) ? service : $"{service} / {category}";
            var amount = salary?.Calculation.Status == SalaryCalculationStatus.Calculated && salary.Calculation.Total is { } total
                ? formatter.Money(total)
                : "未計算";
            return new DayWorkRecordRowViewModel(
                record.Id,
                name,
                formatter.Duration(record.WorkMinutes),
                amount,
                salary?.Calculation.Status == SalaryCalculationStatus.Uncalculated,
                () => navigator.OpenWorkEditorAsync(Date, record.Id, CancellationToken.None),
                () => OpenCalculationDetailsAsync(record.Id),
                () => DeleteRecordAsync(record.Id, name),
                PresentError);
        }).ToArray();
    }

    public Task CopyDayAsync() => RunBusyAsync(async cancellationToken =>
    {
        var sourceDate = DateOnly.FromDateTime(CopySourceDate);
        var preview = await workRecords.PreviewCopyDayAsync(sourceDate, Date, cancellationToken);
        var message = BuildCopyPreviewMessage(preview);
        var blocking = preview.Issues.Any(issue => issue.Code is
            "COPY_DAY_SAME_DATE" or
            "COPY_DAY_SOURCE_MUST_BE_PAST" or
            "COPY_DAY_SOURCE_EMPTY" or
            "WORK_SERVICE_UNAVAILABLE" or
            "WORK_TIME_CATEGORY_UNAVAILABLE" or
            "COPY_DAY_START_REQUIRED_FOR_PREMIUM");
        if (blocking)
        {
            await dialogs.ConfirmAsync("複製できません", message, "閉じる", "キャンセル", cancellationToken);
            return;
        }

        var confirmed = await dialogs.ConfirmAsync(
            "日単位で複製",
            message,
            "複製する",
            "キャンセル",
            cancellationToken);
        if (!confirmed) return;

        var copied = await workRecords.CopyDayAsync(sourceDate, Date, preview.ConfirmationToken, cancellationToken);
        SuccessMessage = $"勤務記録を{copied.Count}件複製しました。";
        try
        {
            await LoadCoreAsync(cancellationToken);
        }
        catch
        {
            SuccessMessage = $"勤務記録を{copied.Count}件複製しました。一覧の再読み込みに失敗しました。再読み込みしてください。";
            throw;
        }
    });

    private string BuildCopyPreviewMessage(CopyDayPreviewDto preview)
    {
        var lines = new List<string>
        {
            $"複製元日: {formatter.Date(preview.SourceDate)}",
            $"複製先日: {formatter.Date(preview.TargetDate)}",
            $"複製される勤務記録: {preview.SourceWorkRecordCount}件",
            $"複製先の既存勤務記録: {preview.TargetExistingWorkRecordCount}件",
        };
        if (preview.UsesDifferentSettingMonth)
            lines.Add($"設定対象年月が{formatter.Month(preview.SourceSettingMonth)}から{formatter.Month(preview.TargetSettingMonth)}へ変わるため、複製先の設定で再計算されます。");
        lines.AddRange(preview.Issues.Select(issue => $"・{issue.Message}"));
        lines.Add("複製先では各勤務を独立した新規記録として保存します。");
        return string.Join(Environment.NewLine, lines);
    }

    public Task DeleteRecordAsync(WorkRecordId id, string displayName) => RunBusyAsync(async cancellationToken =>
    {
        var confirmed = await dialogs.ConfirmAsync(
            "勤務記録を削除",
            $"「{displayName}」を削除します。よろしいですか？",
            "削除",
            "キャンセル",
            cancellationToken);
        if (!confirmed) return;
        await workRecords.DeleteAsync(id, cancellationToken);
        await LoadCoreAsync(cancellationToken);
        SuccessMessage = "勤務記録を削除しました。";
    });
}

public sealed class DayWorkRecordRowViewModel
{
    public DayWorkRecordRowViewModel(
        WorkRecordId id,
        string displayName,
        string durationText,
        string amountText,
        bool isUncalculated,
        Func<Task> edit,
        Func<Task> showDetails,
        Func<Task> delete,
        Action<Exception> onException)
    {
        Id = id;
        DisplayName = displayName;
        DurationText = durationText;
        AmountText = amountText;
        IsUncalculated = isUncalculated;
        EditCommand = new AsyncCommand(edit, onException);
        ShowDetailsCommand = new AsyncCommand(showDetails, onException);
        DeleteCommand = new AsyncCommand(delete, onException);
    }

    public WorkRecordId Id { get; }
    public string DisplayName { get; }
    public string DurationText { get; }
    public string AmountText { get; }
    public bool IsUncalculated { get; }
    public AsyncCommand EditCommand { get; }
    public AsyncCommand ShowDetailsCommand { get; }
    public AsyncCommand DeleteCommand { get; }
}
