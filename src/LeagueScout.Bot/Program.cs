using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using LeagueScout.Application;
using LeagueScout.Application.Publishing;
using LeagueScout.Bot;
using LeagueScout.Bot.Discord;
using LeagueScout.Bot.Workers;
using LeagueScout.Infrastructure;
using LeagueScout.Infrastructure.Persistence;

var builder = Host.CreateApplicationBuilder(args);

var botOptions = BotOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(botOptions);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, botOptions.DatabasePath);

builder.Services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.Guilds,
    LogLevel = LogSeverity.Info,
}));
builder.Services.AddSingleton(sp => new InteractionService(
    sp.GetRequiredService<DiscordSocketClient>(),
    // Sync so ExecuteCommandAsync completes inside the per-interaction DI scope;
    // DiscordBotService offloads each interaction to keep the gateway free.
    new InteractionServiceConfig { LogLevel = LogSeverity.Info, UseCompiledLambda = true, DefaultRunMode = RunMode.Sync }));
builder.Services.AddSingleton<DiscordReadySignal>();
builder.Services.AddSingleton<SyncTrigger>();
builder.Services.AddSingleton<UserCooldown>();
builder.Services.AddSingleton<IEventMessagePublisher, DiscordEventPublisher>();

builder.Services.AddHostedService<DiscordBotService>();
builder.Services.AddHostedService<EventSyncWorker>();

var host = builder.Build();

await MigrateDatabaseAsync(host.Services, botOptions.DatabasePath);
await host.RunAsync();

static async Task MigrateDatabaseAsync(IServiceProvider services, string databasePath)
{
    var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
    if (directory is not null) Directory.CreateDirectory(directory);

    await using var scope = services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    scope.ServiceProvider.GetRequiredService<ILogger<Program>>()
        .LogInformation("Database ready at {DatabasePath}", Path.GetFullPath(databasePath));
}
