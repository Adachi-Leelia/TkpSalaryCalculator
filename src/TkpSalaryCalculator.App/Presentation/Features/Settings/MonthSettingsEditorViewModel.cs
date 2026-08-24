using TkpSalaryCalculator.App.Presentation.Common;
using TkpSalaryCalculator.App.Presentation.Services;
using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.Errors;
using TkpSalaryCalculator.Application.UseCases;
using TkpSalaryCalculator.Domain.Models;

namespace TkpSalaryCalculator.App.Presentation.Features.Settings;

/// <summary>対象年月だけの給与設定を、影響確認後に置換する編集画面の共通処理です。</summary>
public abstract class MonthSettingsEditorViewModel : EditableViewModelBase
{
    private readonly IMonthSettingsUseCase settings;
    private readonly IConfirmationDialogService dialogs;
    private readonly JapaneseDisplayFormatter formatter;
    private readonly ISettingsNavigator navigator;
    private string? firstInvalidField;

    protected MonthSettingsEditorViewModel(
        SettingsMonthContext context,
        IMonthSettingsUseCase settings,
        IConfirmationDialogService dialogs,
        JapaneseDisplayFormatter formatter,
        ISettingsNavigator navigator,
        IUserErrorPresenter errorPresenter) : base(errorPresenter, dialogs)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        this.formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        TrackDataChanges(context.SessionState, TkpSalaryCalculator.App.Navigation.AppDataChangeKind.Settings);
    }

    protected SettingsMonthContext Context { get; }

    protected SettingSnapshot Snapshot => Context.Value?.Snapshot ??
        throw new InvalidOperationException("給与設定が読み込まれていません。");

    public string MonthHeaderText => Context.HeaderText;

    public string? FirstInvalidField
    {
        get => firstInvalidField;
        private set => SetProperty(ref firstInvalidField, value);
    }

    protected async Task LoadEditorAsync(Func<SettingSnapshot, Task> load, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(load);
        var value = await Context.RefreshAsync(cancellationToken);
        OnPropertyChanged(nameof(MonthHeaderText));
        await load(value.Snapshot);
        MarkSaved();
    }

    protected async Task<bool> ConfirmAndSaveAsync(
        SettingSnapshotReplacementDto replacement,
        string title,
        string explanation,
        string successMessage,
        ServicePresetChangeCommand? presetChange,
        CancellationToken cancellationToken)
    {
        FirstInvalidField = null;
        var preview = await settings.PreviewReplacementAsync(Context.SelectedMonth, replacement, cancellationToken);
        SettingsPreviewText.ThrowIfBlocking(preview.Issues);
        var confirmed = await dialogs.ConfirmAsync(
            title,
            SettingsPreviewText.Replacement(preview, formatter, explanation),
            "保存", "キャンセル", cancellationToken);
        if (!confirmed) return false;
        var result = presetChange is null
            ? await settings.CloneAndReplaceAsync(
                Context.SelectedMonth, replacement, preview.ConfirmationToken, cancellationToken)
            : await settings.CloneAndReplaceWithServicePresetAsync(
                Context.SelectedMonth, replacement, preview.ConfirmationToken, presetChange, cancellationToken);
        Context.NotifySettingsChanged(result);
        MarkSaved();
        await navigator.GoBackAsync(successMessage, cancellationToken);
        return true;
    }

    protected void Changed() => MarkDirty();

    protected void ResetFirstInvalidField() => FirstInvalidField = null;

    protected override void OnErrorPresented(Exception exception)
    {
        FirstInvalidField = exception is ApplicationErrorException applicationError
            ? applicationError.Field
            : null;
    }
}

public sealed record RateTypeOption(TkpSalaryCalculator.Domain.ValueObjects.RateType Value, string DisplayName)
{
    public static IReadOnlyList<RateTypeOption> All { get; } =
    [
        new(TkpSalaryCalculator.Domain.ValueObjects.RateType.Hourly, "時給方式"),
        new(TkpSalaryCalculator.Domain.ValueObjects.RateType.FixedPerRecord, "時間区分ごとの固定額"),
    ];
}

public sealed record PremiumTypeOption(TkpSalaryCalculator.Domain.ValueObjects.PremiumCalculationType Value, string DisplayName)
{
    public static IReadOnlyList<PremiumTypeOption> All { get; } =
    [
        new(TkpSalaryCalculator.Domain.ValueObjects.PremiumCalculationType.Percentage, "基本給与への割合加算"),
        new(TkpSalaryCalculator.Domain.ValueObjects.PremiumCalculationType.FixedPerHour, "1時間当たりの固定額"),
        new(TkpSalaryCalculator.Domain.ValueObjects.PremiumCalculationType.FixedPerRecord, "勤務記録1件当たりの固定額"),
    ];
}

public sealed class SelectableServiceViewModel(
    TkpSalaryCalculator.Domain.ValueObjects.ServiceId id,
    string displayName,
    bool isSelected) : ObservableObject
{
    private bool isSelected = isSelected;
    public TkpSalaryCalculator.Domain.ValueObjects.ServiceId Id { get; } = id;
    public string DisplayName { get; } = displayName;
    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }
}
