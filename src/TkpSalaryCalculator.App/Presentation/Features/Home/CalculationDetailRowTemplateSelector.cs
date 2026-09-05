namespace TkpSalaryCalculator.App.Presentation.Features.Home;

/// <summary>計算内訳のフラットな行に対応する再利用可能なテンプレートを選択します。</summary>
public sealed class CalculationDetailRowTemplateSelector : DataTemplateSelector
{
    public required DataTemplate SectionHeaderTemplate { get; set; }
    public required DataTemplate PremiumTotalTemplate { get; set; }
    public required DataTemplate AllowanceTemplate { get; set; }
    public required DataTemplate DayTemplate { get; set; }
    public required DataTemplate VisitTemplate { get; set; }
    public required DataTemplate WorkRecordTemplate { get; set; }
    public required DataTemplate PremiumTemplate { get; set; }
    public required DataTemplate CountBonusTemplate { get; set; }
    public required DataTemplate WorkRecordTotalTemplate { get; set; }

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container) => item switch
    {
        CalculationSectionHeaderRowViewModel => SectionHeaderTemplate,
        CalculationPremiumTotalRowViewModel => PremiumTotalTemplate,
        CalculationAllowanceRowViewModel => AllowanceTemplate,
        CalculationDayRowViewModel => DayTemplate,
        CalculationVisitRowViewModel => VisitTemplate,
        CalculationWorkRecordRowViewModel => WorkRecordTemplate,
        CalculationPremiumRowViewModel => PremiumTemplate,
        CalculationCountBonusRowViewModel => CountBonusTemplate,
        CalculationWorkRecordTotalRowViewModel => WorkRecordTotalTemplate,
        _ => throw new ArgumentOutOfRangeException(nameof(item), item.GetType().FullName, "未対応の計算内訳行です。"),
    };
}
