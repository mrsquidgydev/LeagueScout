namespace LeagueScout.Bot.Discord;

/// <summary>Completes once the Discord gateway connection is ready for the first time.</summary>
public sealed class DiscordReadySignal
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void SetReady() => _ready.TrySetResult();

    public Task WaitAsync(CancellationToken cancellationToken) => _ready.Task.WaitAsync(cancellationToken);
}
