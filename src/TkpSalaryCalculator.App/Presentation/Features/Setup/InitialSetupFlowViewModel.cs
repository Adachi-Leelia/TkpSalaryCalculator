using TkpSalaryCalculator.App.Navigation;
using TkpSalaryCalculator.App.Presentation.Common;

namespace TkpSalaryCalculator.App.Presentation.Features.Setup;

public sealed class InitialSetupFlowViewModel : ObservableObject
{
    public InitialSetupFlowViewModel(IAppSessionState sessionState)
    {
        ArgumentNullException.ThrowIfNull(sessionState);
        var state = sessionState.InitialSetupState;
        ResumeMessage = string.IsNullOrWhiteSpace(state?.Step)
            ? "給与を計算するための初期設定を始めます。"
            : $"保存済みの「{state.Step}」から初期設定を再開します。";
        MissingRequirements = state?.Issues.Count > 0
            ? string.Join(Environment.NewLine, state.Issues.Select(issue => $"・{issue.Message}"))
            : null;
    }

    public string ResumeMessage { get; }

    public string? MissingRequirements { get; }

    public bool HasMissingRequirements => !string.IsNullOrWhiteSpace(MissingRequirements);
}
