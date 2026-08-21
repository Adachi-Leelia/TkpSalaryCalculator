using TkpSalaryCalculator.Application.Contracts;

namespace TkpSalaryCalculator.App.Presentation.Common;

public sealed record IssuePresentation(
    IReadOnlyDictionary<string, string> FieldErrors,
    string? ScreenMessage,
    string? FirstInvalidField);

public sealed class IssuePresenter
{
    public IssuePresentation Present(IEnumerable<IssueDto> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        var values = issues.ToArray();
        var fieldErrors = values
            .Where(issue => !string.IsNullOrWhiteSpace(issue.Field))
            .GroupBy(issue => issue.Field!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => string.Join(Environment.NewLine, group.Select(issue => issue.Message).Distinct(StringComparer.Ordinal)),
                StringComparer.Ordinal);
        var screenMessages = values
            .Where(issue => string.IsNullOrWhiteSpace(issue.Field))
            .Select(issue => issue.Message)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new IssuePresentation(
            fieldErrors,
            screenMessages.Length == 0 ? null : string.Join(Environment.NewLine, screenMessages),
            values.FirstOrDefault(issue => !string.IsNullOrWhiteSpace(issue.Field))?.Field);
    }
}
