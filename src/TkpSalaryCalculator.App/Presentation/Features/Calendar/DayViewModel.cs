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
    private readonly IBasicShiftUseCase? basicShifts;
    private DateOnly date;
    private string dateText = string.Empty;
    private string totalText = "0円";
    private string uncalculatedText = string.Empty;
    private string successMessage = string.Empty;
    private DateTime copySourceDate;
    private DateTime copySourceMaximumDate;
    private IReadOnlyList<DayWorkRecordRowViewModel> records = [];
    private IReadOnlyList<ShiftCandidateRowViewModel> shiftCandidates = [];
    private int existingWorkRecordCount;

    public DayViewModel(
        ISalaryQueryUseCase salaryQuery,
        IWorkRecordUseCase workRecords,
        ICalendarNavigator navigator,
        IConfirmationDialogService dialogs,
        JapaneseDisplayFormatter formatter,
        IUserErrorPresenter errorPresenter,
        IBasicShiftUseCase? basicShifts = null) : base(errorPresenter)
    {
        this.salaryQuery = salaryQuery ?? throw new ArgumentNullException(nameof(salaryQuery));
        this.workRecords = workRecords ?? throw new ArgumentNullException(nameof(workRecords));
        this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        this.dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        this.formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        this.basicShifts = basicShifts;
        ReloadCommand = new AsyncCommand(LoadAsync, PresentError);
        AddWorkCommand = new AsyncCommand(AddWorkAsync, PresentError);
        CopyDayCommand = new AsyncCommand(CopyDayAsync, PresentError);
        ApplyShiftsCommand = new AsyncCommand(ApplyShiftsAsync, PresentError, () => ShiftCandidates.Any(x => x.IsSelected && x.CanChoose));
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
    public IReadOnlyList<ShiftCandidateRowViewModel> ShiftCandidates
    {
        get => shiftCandidates;
        private set
        {
            if (!SetProperty(ref shiftCandidates, value)) return;
            OnPropertyChanged(nameof(HasShiftCandidates));
            ApplyShiftsCommand.NotifyCanExecuteChanged();
        }
    }
    public bool HasShiftCandidates => ShiftCandidates.Count != 0;
    public AsyncCommand ReloadCommand { get; }
    public AsyncCommand AddWorkCommand { get; }
    public AsyncCommand CopyDayCommand { get; }
    public AsyncCommand ApplyShiftsCommand { get; }

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
        var settingsTask = workRecords.GetSettingsForDateAsync(Date, cancellationToken);
        var shiftTask = basicShifts?.PreviewForDateAsync(Date, cancellationToken);
        await Task.WhenAll(new Task[] { salaryTask, recordsTask, settingsTask }.Concat(shiftTask is null ? [] : [shiftTask]));

        var daily = await salaryTask;
        var stored = await recordsTask;
        var monthSettings = await settingsTask;
        var calculations = daily.Records.ToDictionary(x => x.WorkRecord.Id);
        var serviceNames = monthSettings.Snapshot.Services.ToDictionary(x => x.Id, x => x.DisplayName);
        var categoryNames = monthSettings.Snapshot.TimeCategories.ToDictionary(x => x.Id, x => x.DisplayName);
        if (shiftTask is not null)
        {
            var shiftPreview = await shiftTask;
            existingWorkRecordCount = shiftPreview.ExistingWorkRecordCount;
            ShiftCandidates = shiftPreview.Candidates.Select(candidate =>
            {
                var shift = candidate.Shift;
                var service = serviceNames.GetValueOrDefault(shift.ServiceId, "現在の設定にないサービス");
                var category = shift.TimeCategoryId is { } categoryId ? categoryNames.GetValueOrDefault(categoryId) : null;
                var name = string.IsNullOrWhiteSpace(category) ? service : $"{service} / {category}";
                var row = new ShiftCandidateRowViewModel(
                    shift.Id, name, formatter.Duration(shift.WorkMinutes), candidate.CanApply,
                    candidate.CanApply && !candidate.HasSimilarManualRecord,
                    string.Join(Environment.NewLine, candidate.Issues.Select(x => x.Message)));
                row.SelectionChanged += (_, _) => ApplyShiftsCommand.NotifyCanExecuteChanged();
                return row;
            }).ToArray();
        }
        else
        {
            existingWorkRecordCount = stored.Count;
            ShiftCandidates = [];
        }

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

    public Task ApplyShiftsAsync() => RunBusyAsync(async cancellationToken =>
    {
        if (basicShifts is null) return;
        var selected = ShiftCandidates.Where(x => x.CanChoose && x.IsSelected).ToArray();
        if (selected.Length == 0) return;
        var warningLines = selected.Where(x => x.HasWarning).Select(x => $"・{x.DisplayName}: {x.WarningText}");
        var message = $"対象日: {formatter.Date(Date)}{Environment.NewLine}" +
                      $"反映する勤務記録: {selected.Length}件{Environment.NewLine}" +
                      $"既存の勤務記録: {existingWorkRecordCount}件{Environment.NewLine}" +
                      string.Join(Environment.NewLine, selected.Select(x => $"・{x.DisplayName} / {x.DurationText}")) +
                      (warningLines.Any() ? $"{Environment.NewLine}重複の可能性:{Environment.NewLine}{string.Join(Environment.NewLine, warningLines)}" : string.Empty) +
                      $"{Environment.NewLine}{Environment.NewLine}確定するまで給与には含まれません。";
        var confirmed = await dialogs.ConfirmAsync("基本シフトを反映", message, "確定して追加", "キャンセル", cancellationToken);
        if (!confirmed) return;
        var results = await basicShifts.ApplyAsync(new ApplyBasicShiftsCommand(Date, selected.Select(x => x.Id).ToArray()), cancellationToken);
        SuccessMessage = $"基本シフトから勤務記録を{results.Count}件追加しました。";
        await LoadCoreAsync(cancellationToken);
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

public sealed class ShiftCandidateRowViewModel : ObservableObject
{
    private bool isSelected;

    public ShiftCandidateRowViewModel(
        BasicShiftId id,
        string displayName,
        string durationText,
        bool canSelect,
        bool isSelected,
        string warningText)
    {
        Id = id;
        DisplayName = displayName;
        DurationText = durationText;
        CanChoose = canSelect;
        this.isSelected = isSelected;
        WarningText = warningText;
    }

    public event EventHandler? SelectionChanged;
    public BasicShiftId Id { get; }
    public string DisplayName { get; }
    public string DurationText { get; }
    public bool CanChoose { get; }
    public string WarningText { get; }
    public bool HasWarning => !string.IsNullOrWhiteSpace(WarningText);
    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (!CanChoose || !SetProperty(ref isSelected, value)) return;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
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
