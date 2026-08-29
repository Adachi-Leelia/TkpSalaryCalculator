using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace TkpSalaryCalculator.Infrastructure.Sqlite;

/// <summary>同期完了し得る Infrastructure 処理を呼出し元の SynchronizationContext から切り離します。</summary>
internal static class BackgroundOperation
{
    private const int StreamBufferCapacity = 32;
    private static readonly AsyncLocal<int> Depth = new();

    public static Task RunAsync(Func<Task> operation, CancellationToken cancellationToken,
        Action? workerEntered = null) => RunAsync(async () =>
        {
            await operation().ConfigureAwait(false);
            return true;
        }, cancellationToken, workerEntered);

    public static Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken,
        Action? workerEntered = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<T>(cancellationToken);
        if (Depth.Value > 0) return operation();

        return Task.Run(async () =>
        {
            Depth.Value++;
            try
            {
                workerEntered?.Invoke();
                cancellationToken.ThrowIfCancellationRequested();
                return await operation().ConfigureAwait(false);
            }
            finally
            {
                Depth.Value--;
            }
        }, cancellationToken);
    }

    public static async IAsyncEnumerable<T> StreamAsync<T>(
        Func<CancellationToken, IAsyncEnumerable<T>> operation,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        if (Depth.Value > 0)
        {
            await foreach (var item in operation(cancellationToken)
                               .WithCancellation(cancellationToken).ConfigureAwait(false))
                yield return item;
            yield break;
        }

        using var producerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var channel = Channel.CreateBounded<T>(new BoundedChannelOptions(StreamBufferCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
        });
        var producer = Task.Run(async () =>
        {
            Depth.Value++;
            try
            {
                await foreach (var item in operation(producerCancellation.Token)
                                   .WithCancellation(producerCancellation.Token).ConfigureAwait(false))
                    await channel.Writer.WriteAsync(item, producerCancellation.Token).ConfigureAwait(false);
                channel.Writer.TryComplete();
            }
            catch (Exception exception)
            {
                channel.Writer.TryComplete(exception);
            }
            finally
            {
                Depth.Value--;
            }
        }, CancellationToken.None);

        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return item;
        }
        finally
        {
            producerCancellation.Cancel();
            try
            {
                await producer.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (producerCancellation.IsCancellationRequested)
            {
                // The consumer ended enumeration or the caller cancelled it.
            }
        }
    }
}
