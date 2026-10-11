using System.Net.Sockets;
using System.Text;
using ActualLab.IO;

namespace ActualChat.App.Maui;

/// <summary>
/// Keeps the GTK app single-instance: a second launch - from the launcher, or from the desktop
/// handing over a voxt:// link - forwards its link to the running instance over a Unix socket
/// and exits; the running instance shows its window and raises <see cref="UrlOpened"/>.
/// </summary>
internal static class LinuxActivation
{
    private static readonly FilePath SocketPath = GetSocketPath();
    private static ILogger Log => field ??= StaticLog.For(typeof(LinuxActivation));

    // Raised for voxt:// activations; MauiWebAuthenticator completes its sign-in flow on the
    // auth-complete callback.
    public static event Action<string>? UrlOpened;

    public static bool TryForward(string[] args)
    {
        var url = args.FirstOrDefault(x => x.StartsWith($"{MauiSettings.AppScheme}:")) ?? "";
        using var socket = NewSocket();
        try {
            socket.Connect(new UnixDomainSocketEndPoint(SocketPath));
            socket.Send(Encoding.UTF8.GetBytes(url));
            return true;
        }
        catch (SocketException) {
            // Nobody listens: no instance is running, or the socket file outlived a crashed one
            return false;
        }
    }

    public static void Listen()
    {
        File.Delete(SocketPath);
        var listener = NewSocket();
        listener.Bind(new UnixDomainSocketEndPoint(SocketPath));
        listener.Listen();
        _ = Task.Run(() => AcceptActivations(listener));
    }

    // Private methods

    private static async Task AcceptActivations(Socket listener)
    {
        var buffer = new byte[4096];
        while (true)
            try {
                using var connection = await listener.AcceptAsync().ConfigureAwait(false);
                var length = await connection.ReceiveAsync(buffer).ConfigureAwait(false);
                OnActivated(Encoding.UTF8.GetString(buffer, 0, length));
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to accept an activation");
            }
    }

    private static void OnActivated(string url)
    {
        // The link itself stays out of the log: an auth callback is one of them
        Log.LogInformation("Activated by another launch, has link: {HasLink}", !url.IsNullOrEmpty());
        BeginDispatchToMainThread(WindowConfigurator.ShowWindow);
        if (!url.IsNullOrEmpty())
            UrlOpened?.Invoke(url);
    }

    private static Socket NewSocket()
        => new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

    private static FilePath GetSocketPath()
    {
        // The per-user runtime dir is private to the user and cleared on logout
        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        var dir = runtimeDir.IsNullOrEmpty() ? Path.GetTempPath() : runtimeDir;
        return new FilePath(dir) & $"{MauiSettings.ReverseDomain}.sock";
    }
}
