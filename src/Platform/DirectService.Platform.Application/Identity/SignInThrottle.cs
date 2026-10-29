using Microsoft.Extensions.Caching.Memory;

namespace DirectService.Platform.Application.Identity;

/// <summary>
/// Failed sign-ins counted per account and IP address (owner decision 18 Oct 2026, DT-204): <see cref="PerSourceLimit"/> failures from one
/// address within <see cref="Window"/> block that address for that account for <see cref="Window"/>, so one source cannot lock an account
/// out for everyone. The per-account backstop (any source) is Identity's lockout on the user row (<c>FailedSignInCount</c>/<c>LockedUntil</c>,
/// <see cref="PerAccountLimit"/>). Kept in memory: a restart clears source blocks (the backstop stays); a scale-out host needs a shared cache.
/// </summary>
public sealed class SignInThrottle(IMemoryCache cache, TimeProvider clock)
{
    public const int PerSourceLimit = 5;
    public const int PerAccountLimit = 20;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private sealed class Entry
    {
        public int Failures;
        public DateTime WindowStart;
        public DateTime LockedUntil;
    }

    private static string Key(long userId, string? ip) => $"signin:{userId}:{ip ?? "-"}";

    private static string SourcesKey(long userId) => $"signin-sources:{userId}";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Whether this address is blocked for this account.</summary>
    public bool IsBlocked(long userId, string? ip) =>
        cache.TryGetValue<Entry>(Key(userId, ip), out var e) && e is not null && e.LockedUntil > Now;

    /// <summary>Counts a failure from this address; true when it starts a block (to be audited).</summary>
    public bool RecordFailure(long userId, string? ip)
    {
        var now = Now;
        var sources = cache.GetOrCreate(SourcesKey(userId), c => { c.SlidingExpiration = Window + Window; return new HashSet<string>(StringComparer.Ordinal); })!;
        lock (sources) sources.Add(ip ?? "-");
        var entry = cache.GetOrCreate(Key(userId, ip), c =>
        {
            c.SlidingExpiration = Window + Window;
            return new Entry { WindowStart = now };
        })!;
        lock (entry)
        {
            if (now - entry.WindowStart > Window) (entry.Failures, entry.WindowStart) = (0, now);
            entry.Failures++;
            if (entry.Failures < PerSourceLimit || entry.LockedUntil > now) return false;
            entry.LockedUntil = now + Window;
            (entry.Failures, entry.WindowStart) = (0, now);
            return true;
        }
    }

    /// <summary>A successful sign-in from this address clears its count.</summary>
    public void Reset(long userId, string? ip) => cache.Remove(Key(userId, ip));

    /// <summary>An administrator's unlock or a new password clears every address's block for the account.</summary>
    public void ResetAll(long userId)
    {
        if (cache.TryGetValue<HashSet<string>>(SourcesKey(userId), out var sources) && sources is not null)
        {
            lock (sources)
            {
                foreach (var ip in sources) cache.Remove(Key(userId, ip == "-" ? null : ip));
            }
        }
        cache.Remove(SourcesKey(userId));
    }
}
