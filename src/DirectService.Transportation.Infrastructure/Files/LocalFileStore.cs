using System.Threading.Channels;
using DirectService.Transportation.Application.Collaboration;
using Microsoft.Extensions.Configuration;

namespace DirectService.Transportation.Infrastructure.Files;

/// <summary>
/// Files on a local folder (development and single-instance hosting; the hosting provider's object storage replaces it, DEF-01). Keys are
/// relative paths of letters, digits, '-', '_', '.' and '/' only; nothing outside the root is ever read or written.
/// </summary>
public sealed class LocalFileStore(IConfiguration config) : IFileStore
{
    private readonly string _root = Path.GetFullPath(config["Files:Root"] ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "files"));

    private string PathOf(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Contains("..", StringComparison.Ordinal) || key.StartsWith('/')
            || !key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '/'))
            throw new ArgumentException("Invalid file key.", nameof(key));
        var full = Path.GetFullPath(Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar)));
        return full.StartsWith(_root, StringComparison.OrdinalIgnoreCase) ? full : throw new ArgumentException("Invalid file key.", nameof(key));
    }

    public async Task SaveAsync(string key, byte[] content, CancellationToken ct)
    {
        var path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, content, ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct)
    {
        var dir = PathOf(prefix.TrimEnd('/'));
        IReadOnlyList<string> result = Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(_root, f).Replace(Path.DirectorySeparatorChar, '/')).Order(StringComparer.Ordinal).ToList()
            : [];
        return Task.FromResult(result);
    }

    public async Task<byte[]?> ReadAsync(string key, CancellationToken ct)
    {
        var path = PathOf(key);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false) : null;
    }

    public Task<long> SizeAsync(string prefix, CancellationToken ct)
    {
        var dir = PathOf(prefix.TrimEnd('/'));
        return Task.FromResult(Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(Length) : 0L);
    }

    /// <summary>A file's size, 0 when the purge removed it meanwhile.</summary>
    private static long Length(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return 0;
        }
    }

    public Task<int> PurgeAsync(string prefix, TimeSpan age, CancellationToken ct)
    {
        var dir = PathOf(prefix.TrimEnd('/'));
        if (!Directory.Exists(dir)) return Task.FromResult(0);
        var limit = DateTime.UtcNow - age;
        var count = 0;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Where(f => File.GetLastWriteTimeUtc(f) < limit).ToList())
        {
            File.Delete(f);
            count++;
        }
        return Task.FromResult(count);
    }
}

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
