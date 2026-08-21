using TkpSalaryCalculator.Application.Errors;

namespace TkpSalaryCalculator.App.Presentation.Common;

public interface IUserErrorPresenter
{
    string GetMessage(Exception exception);
}

/// <summary>内部情報を画面へ出さず、安全な日本語メッセージだけを返します。</summary>
public sealed class UserErrorPresenter : IUserErrorPresenter
{
    public string GetMessage(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception is ApplicationErrorException applicationError
            ? applicationError.Message
            : "処理に失敗しました。入力内容を保持しています。もう一度お試しください。";
    }
}
