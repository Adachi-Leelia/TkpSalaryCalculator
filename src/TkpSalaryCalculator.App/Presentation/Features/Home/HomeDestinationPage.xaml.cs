using System.Runtime.CompilerServices;

namespace TkpSalaryCalculator.App.Presentation.Features.Home;

/// <summary>後続フェーズの給与期間別画面へ、対象期間を失わず遷移するための受け口です。</summary>
public partial class HomeDestinationPage : ContentPage, IQueryAttributable
{
    public const string DestinationParameter = "destination";
    public const string PayrollPeriodParameter = "payrollPeriod";

    private string destinationTitle = "給与期間";
    private string payrollPeriodText = "未指定";

    public HomeDestinationPage()
    {
        InitializeComponent();
        BindingContext = this;
    }

    public string DestinationTitle
    {
        get => destinationTitle;
        private set => SetProperty(ref destinationTitle, value);
    }

    public string PayrollPeriodText
    {
        get => payrollPeriodText;
        private set
        {
            if (!SetProperty(ref payrollPeriodText, value)) return;
            OnPropertyChanged(nameof(PayrollPeriodAccessibilityText));
        }
    }

    public string PayrollPeriodAccessibilityText => $"対象給与期間: {PayrollPeriodText}";

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        DestinationTitle = ReadText(query, DestinationParameter) ?? "給与期間";
        PayrollPeriodText = ReadText(query, PayrollPeriodParameter) ?? "未指定";
    }

    private static string? ReadText(IDictionary<string, object> query, string key) =>
        query.TryGetValue(key, out var value) ? Uri.UnescapeDataString(value?.ToString() ?? string.Empty) : null;

    private void SetProperty(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value) return;
        field = value;
        OnPropertyChanged(propertyName);
    }
}
