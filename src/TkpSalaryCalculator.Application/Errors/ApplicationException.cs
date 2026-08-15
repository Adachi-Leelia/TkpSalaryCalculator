namespace TkpSalaryCalculator.Application.Errors;

/// <summary>プレゼンテーション層へ安全に通知できる、想定内のアプリケーションエラーを表します。</summary>
public sealed class ApplicationErrorException : Exception
{
    /// <summary>安定したエラーコード、利用者向けメッセージ、および任意の入力項目を指定して生成します。</summary>
    public ApplicationErrorException(string code, string message, string? field = null, Exception? innerException = null)
        : base(message, innerException)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("エラーコードを指定してください。", nameof(code));
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("メッセージを指定してください。", nameof(message));
        Code = code;
        Field = field;
    }

    /// <summary>機械判読可能な安定したエラーコードを取得します。</summary>
    public string Code { get; }

    /// <summary>関連する入力項目を取得します。</summary>
    public string? Field { get; }
}
