using System.Globalization;
using Discord;
using Discord.Interactions;
using LeagueScout.Application.Guilds;
using LeagueScout.Bot.Workers;
using LeagueScout.Domain;

namespace LeagueScout.Bot.Discord.Modules;

[Group("eventbot", "Event bot settings")]
[CommandContextType(InteractionContextType.Guild)]
public sealed class EventBotModule(GuildConfigurationService configurations, SyncTrigger syncTrigger)
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
        if (blocker is null) syncTrigger.Trigger();

        var note = blocker is null
            ? "Saved. A sync has been queued."
            : $"Saved. Events won't be posted yet: {blocker}";

        await RespondAsync(note, embed: BuildStatus(config), ephemeral: true);
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
