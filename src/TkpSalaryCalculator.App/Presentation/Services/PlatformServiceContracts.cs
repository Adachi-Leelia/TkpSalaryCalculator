namespace TkpSalaryCalculator.App.Presentation.Services;

/// <summary>Android のファイル選択結果をストリームとして Presentation 層へ渡します。</summary>
public interface IPlatformDocumentService
{
    Task<Stream?> CreateExportAsync(string suggestedFileName, CancellationToken cancellationToken);

    Task<Stream?> OpenImportAsync(CancellationToken cancellationToken);
}

/// <summary>画面へ表示するアプリの識別情報を提供します。</summary>
public interface IAppInformationService
{
    string Name { get; }

    string DisplayVersion { get; }

    string BuildNumber { get; }
}

/// <summary>破壊的処理後も表示できる単純な結果通知を提供します。</summary>
public interface IUserNotificationService
{
    Task ShowAsync(string title, string message, CancellationToken cancellationToken = default);
}
