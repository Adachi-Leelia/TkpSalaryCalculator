using TkpSalaryCalculator.Application.Contracts;

namespace TkpSalaryCalculator.Application.Ports;

/// <summary>Defines an atomic application transaction boundary.</summary>
public interface ITransactionRunner
{
    /// <summary>Runs all operations in one transaction and rolls back if the callback fails or is cancelled.</summary>
    Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken);

    /// <summary>Runs all operations in one transaction and returns the callback result after commit.</summary>
    Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken);
}

/// <summary>Supplies UTC instants to Application without relying directly on the system clock.</summary>
public interface IUtcClock
{
    /// <summary>Gets the current UTC instant.</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>Serializes logical export records as a single UTF-8 JSON document incrementally.</summary>
public interface IJsonExportStream
{
    /// <summary>Writes the header and records without materializing the full sequence.</summary>
    /// <param name="destination">A writable stream owned by the caller. The implementation must leave it open and must not dispose it.</param>
    /// <param name="header">The export header.</param>
    /// <param name="records">The asynchronously produced logical records.</param>
    /// <param name="cancellationToken">Stops enumeration and stream I/O.</param>
    Task WriteAsync(
        Stream destination,
        ExportDocumentHeader header,
        IAsyncEnumerable<DataTransferRecord> records,
        CancellationToken cancellationToken);
}

/// <summary>Parses one UTF-8 JSON export document incrementally.</summary>
public interface IJsonImportStream
{
    /// <summary>Yields logical records in document order without creating a full object graph.</summary>
    /// <param name="source">A readable stream owned by the caller. The implementation must leave it open, must not dispose it, and must not require seeking.</param>
    /// <param name="cancellationToken">Stops parsing and stream I/O.</param>
    /// <returns>A sequence whose metadata record precedes data-section records.</returns>
    IAsyncEnumerable<DataTransferRecord> ReadAsync(
        Stream source,
        CancellationToken cancellationToken);
}

/// <summary>Streams the live logical data set in export order.</summary>
public interface IExportDataSource
{
    /// <summary>Yields only data required to reproduce settings, shifts, work, allowances, and holiday results.</summary>
    IAsyncEnumerable<DataTransferRecord> StreamAsync(CancellationToken cancellationToken);
}

/// <summary>Stages and validates imported records separately from the live data set.</summary>
public interface IImportStagingRepository
{
    /// <summary>Creates an empty staging area.</summary>
    Task<PreparedImportId> CreateAsync(CancellationToken cancellationToken);

    /// <summary>Appends a bounded batch without requiring all import records in memory.</summary>
    Task AppendBatchAsync(
        PreparedImportId preparedImportId,
        IReadOnlyList<DataTransferRecord> records,
        CancellationToken cancellationToken);

    /// <summary>Validates counts, values, versions, uniqueness, and referential integrity of the complete staging area.</summary>
    Task<ImportPreviewDto> ValidateAsync(
        PreparedImportId preparedImportId,
        CancellationToken cancellationToken);

    /// <summary>Atomically replaces all live data from a validated staging area.</summary>
    Task ReplaceLiveDataAsync(
        PreparedImportId preparedImportId,
        DateTimeOffset importedAtUtc,
        CancellationToken cancellationToken);

    /// <summary>Deletes a staging area and any temporary files.</summary>
    Task DiscardAsync(PreparedImportId preparedImportId, CancellationToken cancellationToken);

    /// <summary>Deletes abandoned staging data left by an interrupted earlier run.</summary>
    Task DiscardAbandonedAsync(CancellationToken cancellationToken);
}
