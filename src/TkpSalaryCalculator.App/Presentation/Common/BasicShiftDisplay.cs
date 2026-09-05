using TkpSalaryCalculator.Application.Contracts;
using TkpSalaryCalculator.Domain.ValueObjects;

namespace TkpSalaryCalculator.App.Presentation.Common;

/// <summary>一覧と反映確認で全タスクを同じ順序・表記で表示します。</summary>
public static class BasicShiftDisplay
{
    public static (string Name, string Time) Summarize(BasicShiftDto shift,
        IReadOnlyDictionary<ServiceId, string> services, IReadOnlyDictionary<TimeCategoryId, string> categories,
        JapaneseDisplayFormatter formatter)
    {
        var ordered = shift.Tasks.OrderBy(task => task.DisplayOrder.Value).ToArray();
        var names = ordered.Select(task =>
        {
            var service = services.GetValueOrDefault(task.ServiceId, "現在の設定にないサービス");
            var category = task.TimeCategoryId is { } id ? categories.GetValueOrDefault(id, "現在の設定にない時間区分") : "任意時間";
            return $"{service} / {category}";
        });
        var times = ordered.Select(task =>
        {
            var time = task.StartTime is { } start && task.EndTime is { } end
                ? $"{formatter.Time(start)}～{formatter.Time(end)}{(end.Value <= start.Value ? "（翌日）" : string.Empty)} / {formatter.Duration(task.WorkMinutes)}"
                : formatter.Duration(task.WorkMinutes);
            return ordered.Length == 1 ? time : $"タスク {task.DisplayOrder.Value + 1}: {time}";
        });
        var name = string.Join("、", names);
        if (ordered.Length > 1) name = $"タスク {ordered.Length}件: {name}";
        return (name, string.Join(Environment.NewLine, times));
    }
}
