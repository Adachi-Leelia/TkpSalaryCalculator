namespace TkpSalaryCalculator.App.Presentation.Features.Settings;

internal static class SettingsEditorFocus
{
    public static async Task FocusAsync(ScrollView scrollView, VisualElement? target)
    {
        ArgumentNullException.ThrowIfNull(scrollView);
        if (target is null) return;

        await Task.Yield();
        if (!target.IsVisible) return;
        await scrollView.ScrollToAsync(target, ScrollToPosition.MakeVisible, true);
        target.Focus();
    }
}
