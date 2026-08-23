using TkpSalaryCalculator.App.Navigation;
using TkpSalaryCalculator.App.Presentation.Common;
using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Application.UseCases;
using TkpSalaryCalculator.Domain.Models;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.App.Presentation.Features.Settings;

/// <summary>すべての給与設定画面で共有する、設定対象年月と読取済みスナップショットです。</summary>
public sealed class SettingsMonthContext(
    IMonthSettingsUseCase settings,
    IAppSessionState sessionState,
    JapaneseDisplayFormatter formatter) : ObservableObject
{
    private readonly IMonthSettingsUseCase settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly IAppSessionState sessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));
    private readonly JapaneseDisplayFormatter formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
    private MonthSettingsDto? value;
    private long? loadedGeneration;

    public YearMonth SelectedMonth => sessionState.SettingsMonth;

    internal IAppSessionState SessionState => sessionState;

    public string HeaderText => formatter.SettingsMonth(SelectedMonth);

    public MonthSettingsDto? Value
    {
        get => value;
        private set => SetProperty(ref this.value, value);
    }

    public async Task<MonthSettingsDto> RefreshAsync(CancellationToken cancellationToken)
    {
        var generation = sessionState.GetDataGeneration(AppDataChangeKind.Settings);
        if (Value is { } cached && cached.YearMonth == SelectedMonth && loadedGeneration == generation)
            return cached;

        var loaded = await settings.GetAsync(SelectedMonth, cancellationToken);
        Accept(loaded, generation);
        return loaded;
    }

    public void MoveWithoutLoading(int monthOffset)
    {
        sessionState.SettingsMonth = SelectedMonth.AddMonths(monthOffset);
        OnPropertyChanged(nameof(SelectedMonth));
        OnPropertyChanged(nameof(HeaderText));
    }

    public void Accept(MonthSettingsDto loaded)
        => Accept(loaded, sessionState.GetDataGeneration(AppDataChangeKind.Settings));

    public void NotifySettingsChanged(MonthSettingsDto loaded)
    {
        sessionState.NotifyDataChanged(AppDataChangeKind.Settings);
        Accept(loaded);
    }

    private void Accept(MonthSettingsDto loaded, long generation)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        if (loaded.YearMonth != SelectedMonth)
            throw new InvalidOperationException("設定対象年月と読込結果が一致しません。");
        Value = loaded;
        loadedGeneration = generation;
    }

    public static SettingSnapshotReplacementDto ToReplacement(SettingSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new(snapshot.Services, snapshot.TimeCategories, snapshot.Rates, snapshot.Premiums, snapshot.CountBonuses);
    }
}

internal static class SettingsPreviewText
{
    public static string Replacement(SettingReplacementPreviewDto preview, JapaneseDisplayFormatter formatter, string explanation)
    {
        var difference = preview.ReplacementCalculatedSubtotal.Value - preview.CurrentCalculatedSubtotal.Value;
        var sign = difference > 0 ? "+" : string.Empty;
        var lines = new List<string>
        {
            explanation,
            $"設定対象年月: {formatter.Month(preview.TargetMonth)}",
            $"影響する勤務記録: {preview.AffectedWorkRecordCount}件",
            $"変更前の見込み小計: {formatter.Money(preview.CurrentCalculatedSubtotal)}",
            $"変更後の見込み小計: {formatter.Money(preview.ReplacementCalculatedSubtotal)}",
            $"見込み差額: {sign}{formatter.Money(new YenAmount(difference))}",
        };
        if (preview.ResultingUncalculatedCount > 0)
            lines.Add($"変更後に未計算となる勤務記録: {preview.ResultingUncalculatedCount}件");
        return string.Join(Environment.NewLine, lines);
    }

    public static void ThrowIfBlocking(IReadOnlyList<IssueDto> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        if (issues.Count == 0) return;
        throw new TkpSalaryCalculator.Application.Errors.ApplicationErrorException(
            "SETTINGS_PREVIEW_BLOCKED", string.Join(Environment.NewLine, issues.Select(value => value.Message)),
            issues.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value.Field))?.Field);
    }
}
