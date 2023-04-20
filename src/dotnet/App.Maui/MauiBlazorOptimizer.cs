using ActualChat.Chat;
using ActualChat.Users;

namespace ActualChat.App.Maui;

public class MauiBlazorOptimizer
{
    private static readonly Tracer _tracer = Tracer.Default[nameof(MauiBlazorOptimizer)];

    private IServiceProvider Services { get; }

    public MauiBlazorOptimizer(IServiceProvider services)
        => Services = services;

    public async Task WarmupServices()
    {
        using var _ = _tracer.Region(nameof(WarmupServices));
        var warmupTasks = new [] {
            Task.Run(() => {
                using var _1 = _tracer.Region("Warmup serializers");
                WarmupSerializer(new Account());
                WarmupSerializer(new AccountFull());
                WarmupSerializer(new Contacts.Contact());
                WarmupSerializer(new Chat.Chat());
                WarmupSerializer(new ChatTile());
                WarmupSerializer(new Mention());
                WarmupSerializer(new Reaction());
                WarmupSerializer(new Author());
            }),
        };
        try {
            await Task.WhenAll(warmupTasks).ConfigureAwait(false);
        }
        catch (Exception e) {
            _tracer.Point($"{nameof(WarmupServices)} failed, error: " + e);
        }
    }

    private void WarmupSerializer<T>(T value)
    {
        var s = TextSerializer.Default;
        var text = "";

        // Write
        try {
            text = s.Write(value);
        }
        catch {
            // Intended
        }

        // Read
        try {
            _ = s.Read<T>(text);
        }
        catch {
            // Intended
        }
    }

    private void WarmupReplicaService<T>()
        where T: class, IComputeService
        => Services.GetRequiredService<T>();

    private void WarmupComputeService<T>()
        where T: class, IComputeService
        => Services.GetRequiredService<T>();
}
