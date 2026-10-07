using System.Globalization;
using Discord;
using Discord.Interactions;
using LeagueScout.Application.Caching;
using LeagueScout.Application.Guilds;
using LeagueScout.Bot.Workers;
using LeagueScout.Domain;

namespace LeagueScout.Bot.Discord.Modules;

[Group("eventbot", "Event bot settings")]
[CommandContextType(InteractionContextType.Guild)]
public sealed class EventBotModule(
    GuildConfigurationService configurations,
    EventCacheDiagnosticsService diagnostics,
    SyncTrigger syncTrigger,
    CacheRefreshTrigger cacheRefreshTrigger)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("status", "Show this server's event bot configuration")]
    public async Task StatusAsync()
    {
        var config = await configurations.GetAsync(Context.Guild.Id);
        if (config is null)
        {
            await RespondAsync(
                "The event bot isn't configured here yet. A server manager can run `/eventbot configure`.",
                ephemeral: true);
            return;
        }

        await RespondAsync(embed: BuildStatus(config), ephemeral: true);
    }

    [SlashCommand("configure", "Change event bot settings (only the options you provide are changed)")]
    [DefaultMemberPermissions(GuildPermission.ManageGuild)]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public async Task ConfigureAsync(
        [Summary("channel", "Channel where events are posted")][ChannelTypes(ChannelType.Text)] ITextChannel? channel = null,
        [Summary("country", "Two-letter country code, e.g. US")] string? country = null,
        [Summary("regions", "Comma-separated state/region names, e.g. Texas,Oklahoma")] string? regions = null,
        [Summary("latitude", "Search centre latitude")][MinValue(-90)][MaxValue(90)] double? latitude = null,
        [Summary("longitude", "Search centre longitude")][MinValue(-180)][MaxValue(180)] double? longitude = null,
        [Summary("radius", "Search radius; 0 clears the radius search")][MinValue(0)][MaxValue(1000)] double? radius = null,
        [Summary("unit", "Radius unit")] DistanceUnit? unit = null,
        [Summary("include_challenges", "Post League Challenges")] bool? includeChallenges = null,
        [Summary("include_cups", "Post League Cups")] bool? includeCups = null,
        [Summary("lookahead_days", "How many days ahead to look")][MinValue(1)][MaxValue(120)] int? lookaheadDays = null,
        [Summary("enabled", "Turn event posting on or off")] bool? enabled = null)
    {
        var config = await configurations.UpdateAsync(Context.Guild.Id, c =>
        {
            if (channel is not null) c.EventChannelId = channel.Id;
            if (country is not null) c.Country = string.IsNullOrWhiteSpace(country) ? null : country.Trim().ToUpperInvariant();
            if (regions is not null)
            {
                c.Regions = regions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            }

            if (latitude is not null) c.Latitude = latitude;
            if (longitude is not null) c.Longitude = longitude;
            if (radius is not null)
            {
                if (radius > 0)
                {
                    c.Radius = radius;
                }
                else
                {
                    c.Radius = null;
                    c.Latitude = null;
                    c.Longitude = null;
                }
            }

            if (unit is not null) c.RadiusUnit = unit.Value;
            if (includeChallenges is not null) c.IncludeChallenges = includeChallenges.Value;
            if (includeCups is not null) c.IncludeCups = includeCups.Value;
            if (lookaheadDays is not null) c.LookAheadDays = lookaheadDays.Value;
            if (enabled is not null) c.Enabled = enabled.Value;
        });

        var blocker = config.GetSyncBlocker();
        if (blocker is null)
        {
            // Post matching events already in the shared cache now. The refresh worker requests
            // PokéData only if this server's dataset has never been fetched or is due.
            syncTrigger.Trigger();
            cacheRefreshTrigger.Trigger();
        }

        var note = blocker is null
            ? "Saved. A sync has been queued."
            : $"Saved. Events won't be posted yet: {blocker}";

        await RespondAsync(note, embed: BuildStatus(config), ephemeral: true);
    }

    [SlashCommand("diagnostics", "Show PokéData cache health for this server")]
    [DefaultMemberPermissions(GuildPermission.ManageGuild)]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public async Task DiagnosticsAsync()
    {
        var report = await diagnostics.GetAsync(Context.Guild.Id);
        await RespondAsync(embed: BuildDiagnostics(report), ephemeral: true);
    }

    private static Embed BuildDiagnostics(EventCacheDiagnostics report)
    {
        static string When(DateTime? utc) => utc is { } value ? EventEmbedBuilder.Timestamp(value, 'R') : "Never";

        var embed = new EmbedBuilder().WithTitle("PokéData cache");
        var dataset = report.Dataset;

        if (dataset is null)
        {
            embed.WithDescription("This server has no location set, so it reads no PokéData dataset.");
        }
        else
        {
            var health = dataset.Health switch
            {
                CacheHealth.Healthy => "✅ Healthy",
                CacheHealth.Retrying => "⚠️ Last refresh failed; serving cached data",
                CacheHealth.Stale => "⚠️ Stale; serving cached data",
                _ => "⏳ Not refreshed yet",
            };

            var age = dataset.CacheAge is { } a ? $"{(int)a.TotalHours}h {a.Minutes}m" : "None";
            var failures = dataset.LastFailureStatusCode is { } status && dataset.ConsecutiveFailures > 0
                ? $"{dataset.ConsecutiveFailures} (HTTP {status})"
                : dataset.ConsecutiveFailures.ToString(CultureInfo.InvariantCulture);

            embed
                .AddField("Dataset", Format.Code(dataset.DatasetKey), inline: true)
                .AddField("Health", health, inline: true)
                .AddField("Last attempted refresh", When(dataset.LastAttemptAt), inline: true)
                .AddField("Last successful refresh", When(dataset.LastSuccessAt), inline: true)
                .AddField("Cache age", age, inline: true)
                .AddField("Next allowed refresh", When(dataset.NextAllowedRequestAt), inline: true)
                .AddField("Events in last refresh", dataset.LastResultCount, inline: true)
                .AddField("Consecutive failures", failures, inline: true);
        }

        var counters = report.Counters;
        return embed
            .AddField("Cached events", report.CachedEventCount, inline: true)
            .AddField("Upcoming active events", report.UpcomingActiveEventCount, inline: true)
            .AddField("Matching upcoming events", report.MatchingUpcomingEventCount?.ToString(CultureInfo.InvariantCulture) ?? "n/a", inline: true)
            .AddField("Last server sync", When(report.LastGuildSyncAt), inline: true)
            .WithFooter(string.Create(CultureInfo.InvariantCulture,
                $"Since start: {counters.UpstreamRequests} requests, {counters.Failures} failed " +
                $"({counters.RateLimited}× 429, {counters.ServerErrors}× 5xx), " +
                $"{counters.CacheHits} cache hits, {counters.CacheMisses} misses"))
            .Build();
    }

    private static Embed BuildStatus(GuildConfiguration config)
    {
        string location;
        if (config.HasRadiusFilter)
        {
            var unit = config.RadiusUnit == DistanceUnit.Kilometers ? "km" : "mi";
            location = string.Create(CultureInfo.InvariantCulture,
                $"Within {config.Radius} {unit} of {config.Latitude:0.####}, {config.Longitude:0.####}");
        }
        else if (config.HasRegionFilter)
        {
            location = config.Regions.Count > 0 ? $"{string.Join(", ", config.Regions)} ({config.Country})" : $"All of {config.Country}";
        }
        else
        {
            location = "Not set";
        }

        var types = new List<string>();
        if (config.IncludeChallenges) types.Add("League Challenges");
        if (config.IncludeCups) types.Add("League Cups");

        var blocker = config.GetSyncBlocker();

        return new EmbedBuilder()
            .WithTitle("Event bot configuration")
            .AddField("Status", blocker is null ? "✅ Active" : $"⏸️ {blocker}")
            .AddField("Channel", config.EventChannelId is { } id ? MentionUtils.MentionChannel(id) : "Not set", inline: true)
            .AddField("Look-ahead", $"{config.LookAheadDays} days", inline: true)
            .AddField("Location", Format.Sanitize(location))
            .AddField("Event types", types.Count > 0 ? string.Join(", ", types) : "None")
            .Build();
    }
}
