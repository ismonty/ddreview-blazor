using System.Collections.Concurrent;

namespace DDReview.Core.Security;

/// <summary>Simple in-memory lockout: too many failures for a key (user+ip) blocks further attempts for a while.</summary>
public sealed class LoginThrottle(int maxFailures = 5, TimeSpan? window = null, Func<DateTime>? clock = null)
{
    private readonly TimeSpan _window = window ?? TimeSpan.FromMinutes(15);
    private readonly Func<DateTime> _now = clock ?? (() => DateTime.UtcNow);
    private readonly ConcurrentDictionary<string, (int Count, DateTime First)> _state = new();

    public bool IsLockedOut(string key) =>
        _state.TryGetValue(key, out var s) && _now() - s.First < _window && s.Count >= maxFailures;

    public void RecordFailure(string key) =>
        _state.AddOrUpdate(key, _ => (1, _now()),
            (_, s) => _now() - s.First >= _window ? (1, _now()) : (s.Count + 1, s.First));

    public void Reset(string key) => _state.TryRemove(key, out _);
}
