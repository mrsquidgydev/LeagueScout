using Discord.Interactions;
using Discord.WebSocket;
using LeagueScout.Application.Rsvps;

namespace LeagueScout.Bot.Discord.Modules;

public sealed class RsvpModule(RsvpService rsvpService, ILogger<RsvpModule> logger)
    : InteractionModuleBase<SocketInteractionContext>
{
    private SocketMessageComponent Component => (SocketMessageComponent)Context.Interaction;

    [ComponentInteraction(RsvpButtonId.Prefix + ":*:*", ignoreGroupNames: true)]
    public async Task HandleAsync(string eventToken, string statusToken)
    {
        if (Context.Guild is null)
        {
            await RespondAsync("RSVPs only work inside a server.", ephemeral: true);
            return;
        }

        // Custom IDs come from the client: parse strictly and verify against the database.
        if (!RsvpButtonId.TryParseEventId(eventToken, out var eventId)
            || !RsvpButtonId.TryParseStatus(statusToken, out var status))
        {
            logger.LogWarning("Rejected malformed RSVP custom ID {CustomId}", Component.Data.CustomId);
            await RespondAsync("That button isn't valid anymore.", ephemeral: true);
            return;
        }

        var result = await rsvpService.SetRsvpAsync(
            Context.Guild.Id, Component.Message.Id, Context.User.Id, eventId, status);

        if (!result.Succeeded)
        {
            await RespondAsync(result.Error, ephemeral: true);
            return;
        }

        // Refresh counts on the original post, then confirm privately to the user.
        await Component.UpdateAsync(m =>
        {
            m.Embed = EventEmbedBuilder.BuildEmbed(result.Event!, result.Summary!);
            m.Components = EventEmbedBuilder.BuildComponents(result.Event!);
        });

        await FollowupAsync(
            $"You're marked as **{EventEmbedBuilder.StatusLabel(status)}** for {EventEmbedBuilder.ShortName(result.Event!)}.",
            ephemeral: true);
    }
}
