using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;

namespace LeagueScout.Bot.Discord;

/// <summary>Connects to Discord, registers slash commands and dispatches interactions.</summary>
public sealed class DiscordBotService(
    DiscordSocketClient client,
    InteractionService interactions,
    IServiceProvider services,
    DiscordReadySignal readySignal,
    BotOptions options,
    ILogger<DiscordBotService> logger,
    ILoggerFactory loggerFactory) : IHostedService
{
    private readonly ILogger _discordLogger = loggerFactory.CreateLogger("Discord");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        client.Log += LogAsync;
        interactions.Log += LogAsync;
        client.Ready += OnReadyAsync;
        client.InteractionCreated += OnInteractionAsync;
        interactions.InteractionExecuted += OnInteractionExecutedAsync;

        await interactions.AddModulesAsync(Assembly.GetExecutingAssembly(), services);

        await client.LoginAsync(TokenType.Bot, options.DiscordToken);
        await client.StartAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        client.InteractionCreated -= OnInteractionAsync;
        await client.StopAsync();
        await client.LogoutAsync();
    }

    private async Task OnReadyAsync()
    {
        try
        {
            if (options.CommandGuildId is { } guildId)
            {
                await interactions.RegisterCommandsToGuildAsync(guildId);
                logger.LogInformation("Slash commands registered to guild {GuildId}", guildId);
            }
            else
            {
                await interactions.RegisterCommandsGloballyAsync();
                logger.LogInformation("Slash commands registered globally");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Slash command registration failed");
        }

        logger.LogInformation("Discord connected as {BotUser} in {GuildCount} guild(s)", client.CurrentUser, client.Guilds.Count);
        readySignal.SetReady();
    }

    private async Task OnInteractionAsync(SocketInteraction interaction)
    {
        // Each interaction gets its own DI scope (and DbContext).
        await using var scope = services.CreateAsyncScope();
        try
        {
            var context = new SocketInteractionContext(client, interaction);
            await interactions.ExecuteCommandAsync(context, scope.ServiceProvider);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error executing interaction {InteractionId}", interaction.Id);
        }
    }

    private async Task OnInteractionExecutedAsync(ICommandInfo command, IInteractionContext context, IResult result)
    {
        if (result.IsSuccess) return;

        logger.LogWarning(
            "Interaction {Command} failed for user {UserId} in guild {GuildId}: {Error} {Reason}",
            command?.Name, context.User.Id, context.Guild?.Id, result.Error, result.ErrorReason);

        if (context.Interaction.HasResponded) return;

        var message = result.Error == InteractionCommandError.UnmetPrecondition
            ? result.ErrorReason
            : "Something went wrong handling that request.";

        try
        {
            await context.Interaction.RespondAsync(message, ephemeral: true);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not send interaction error response");
        }
    }

    private Task LogAsync(LogMessage message)
    {
        var level = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            _ => LogLevel.Trace,
        };

        _discordLogger.Log(level, message.Exception, "[{Source}] {Message}", message.Source, message.Message);
        return Task.CompletedTask;
    }
}
