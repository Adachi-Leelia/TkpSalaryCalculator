using TkpSalaryCalculator.Application.Contracts;

namespace TkpSalaryCalculator.Application.Ports;

/// <summary>アプリケーションの原子的なトランザクション境界を定義します。</summary>
public interface ITransactionRunner
{
    /// <summary>すべての処理を 1 つのトランザクションで実行し、コールバックの失敗または取り消し時にロールバックします。</summary>
    Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken);

    /// <summary>すべての処理を 1 つのトランザクションで実行し、コミット後にコールバックの結果を返します。</summary>
    Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken);
}

/// <summary>システム時計に直接依存せず、アプリケーション層へ UTC 日時を提供します。</summary>
public interface IUtcClock
{
    /// <summary>現在の UTC 日時を取得します。</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>論理エクスポートレコードを単一の UTF-8 JSON 文書へ逐次シリアル化します。</summary>
public interface IJsonExportStream
{
    /// <summary>シーケンス全体を実体化せず、ヘッダーとレコードを書き込みます。</summary>
    /// <param name="destination">呼び出し元が所有する書き込み可能なストリーム。実装側で破棄せず、開いたままにする必要があります。</param>
    /// <param name="header">エクスポートヘッダー。</param>
    /// <param name="records">非同期に生成される論理レコード。</param>
    /// <param name="cancellationToken">列挙処理とストリーム入出力を中止します。</param>
    Task WriteAsync(
        Stream destination,
        ExportDocumentHeader header,
        IAsyncEnumerable<DataTransferRecord> records,
        CancellationToken cancellationToken);
}

/// <summary>1 件の UTF-8 JSON エクスポート文書を逐次解析します。</summary>
public interface IJsonImportStream
{
    /// <summary>完全なオブジェクトグラフを作成せず、文書内の順序で論理レコードを返します。</summary>
    /// <param name="source">呼び出し元が所有する読み取り可能なストリーム。実装側で破棄せず、開いたままにし、シークを要求しない必要があります。</param>
    /// <param name="cancellationToken">解析処理とストリーム入出力を中止します。</param>
    /// <returns>データセクションのレコードより先にメタデータレコードを返すシーケンス。</returns>
    IAsyncEnumerable<DataTransferRecord> ReadAsync(
        Stream source,
        CancellationToken cancellationToken);
}

/// <summary>本番の論理データセットをエクスポート順でストリーミングします。</summary>
public interface IExportDataSource
{
    /// <summary>設定、シフト、勤務、手当、および祝日結果の再現に必要なデータだけを返します。</summary>
    IAsyncEnumerable<DataTransferRecord> StreamAsync(CancellationToken cancellationToken);
}

/// <summary>インポートしたレコードを本番データセットから分離して準備および検証します。</summary>
public interface IImportStagingRepository
{
    /// <summary>空の準備領域を作成します。</summary>
    Task<PreparedImportId> CreateAsync(CancellationToken cancellationToken);

    /// <summary>すべてのインポートレコードをメモリへ読み込まず、上限付きのバッチを追加します。</summary>
    Task AppendBatchAsync(
        PreparedImportId preparedImportId,
        IReadOnlyList<DataTransferRecord> records,
        CancellationToken cancellationToken);

    /// <summary>準備領域全体の件数、値、バージョン、一意性、および参照整合性を検証します。</summary>
    Task<ImportPreviewDto> ValidateAsync(
        PreparedImportId preparedImportId,
        CancellationToken cancellationToken);

    /// <summary>検証済みの準備領域を使用して、本番データをすべて原子的に置換します。</summary>
    Task ReplaceLiveDataAsync(
        PreparedImportId preparedImportId,
        DateTimeOffset importedAtUtc,
        CancellationToken cancellationToken);

    /// <summary>準備領域とすべての一時ファイルを削除します。</summary>
    Task DiscardAsync(PreparedImportId preparedImportId, CancellationToken cancellationToken);

    /// <summary>以前の処理中断によって残された未使用の準備データを削除します。</summary>
    Task DiscardAbandonedAsync(CancellationToken cancellationToken);
}
