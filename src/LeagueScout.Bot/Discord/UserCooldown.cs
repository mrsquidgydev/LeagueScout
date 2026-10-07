using Microsoft.Extensions.Caching.Memory;

namespace LeagueScout.Bot.Discord;

/// <summary>Per-user rate limit for commands that call external services.</summary>
public sealed class UserCooldown(IMemoryCache cache, TimeProvider clock)
{
    /// <summary>Starts a cooldown, or returns false with the time left on the current one.</summary>
    public bool TryStart(string command, ulong userId, TimeSpan duration, out TimeSpan remaining)
    {
        var key = new CooldownKey(command, userId);
        var now = clock.GetUtcNow();

        if (cache.TryGetValue(key, out DateTimeOffset until) && until > now)
        {
            remaining = until - now;
            return false;
        }

        cache.Set(key, now + duration, duration);
        remaining = TimeSpan.Zero;
        return true;
    }

    private readonly record struct CooldownKey(string Command, ulong UserId);
}
