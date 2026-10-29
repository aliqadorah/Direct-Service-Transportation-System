using System.Threading.Channels;

namespace DirectService.Shared.Infrastructure.Files;

/// <summary>Bounded in-process queue of export jobs (1,000 waiting at most; a full queue refuses with 429).</summary>
public sealed class ChannelExportQueue : IExportQueue
{
    /// <summary>Waiting jobs per user (review M8): a user may queue at most this many at once.</summary>
    public const int PerUser = 3;

    private readonly Channel<ExportJob> _channel = Channel.CreateBounded<ExportJob>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.Wait });
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, int> _waiting = new();

    public bool Enqueue(ExportJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (_waiting.AddOrUpdate(job.UserId, 1, (_, n) => n + 1) > PerUser || !_channel.Writer.TryWrite(job))
        {
            Done(job);
            return false;
        }
        return true;
    }

    private void Done(ExportJob job) => _waiting.AddOrUpdate(job.UserId, 0, (_, n) => Math.Max(0, n - 1));

    public async IAsyncEnumerable<ExportJob> ReadAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var job in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            Done(job);
            yield return job;
        }
    }

    public bool TryRead(out ExportJob? job)
    {
        if (!_channel.Reader.TryRead(out job)) return false;
        Done(job);
        return true;
    }
}
