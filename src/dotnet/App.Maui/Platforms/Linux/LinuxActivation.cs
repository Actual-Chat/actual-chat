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
    private static readonly TimeSpan HandoverTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan HandoverRetryDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan AcceptRetryDelay = TimeSpan.FromSeconds(1);

    // The per-user runtime dir is private to the user and cleared on logout
    private static readonly FilePath RuntimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? "";
    private static readonly FilePath SocketPath = RuntimeDir & $"{MauiSettings.ReverseDomain}.sock";
    private static readonly FilePath LockPath = RuntimeDir & $"{MauiSettings.ReverseDomain}.lock";
    private static readonly CancellationTokenSource StopTokenSource = new();
    private static FileStream? _lock;
    private static Socket? _listener;

    private static ILogger Log
        // Not cached: the first use may precede the logging setup
        => StaticLog.For(typeof(LinuxActivation));

    // Raised for voxt:// activations; MauiWebAuthenticator completes its sign-in flow on the
    // auth-complete callback.
    public static event Action<string>? UrlOpened;

    public static bool TryStart(string[] args)
    {
        // False means this launch is over: its link went to the running instance, or that instance
        // holds the lock, but doesn't answer
        if (RuntimeDir.IsEmpty)
            return true; // No place for a private socket: every launch is an instance of its own

        var url = args.FirstOrDefault(x => x.StartsWith($"{MauiSettings.AppScheme}:")) ?? "";
        var startedAt = CpuTimestamp.Now;
        while (true) {
            if (TryLock()) {
                // The socket opens here rather than in Listen, so the links arriving while the app
                // starts wait in its backlog. Only the lock holder may delete a stale socket file.
                File.Delete(SocketPath);
                _listener = NewSocket();
                _listener.Bind(new UnixDomainSocketEndPoint(SocketPath));
                _listener.Listen();
                return true;
            }

            if (TryForward(url))
                return false;

            // The lock holder is either about to open its socket or about to exit
            if (startedAt.Elapsed > HandoverTimeout) {
                Console.Error.WriteLine("Another instance is running, but it doesn't accept activations.");
                return false;
            }

            Thread.Sleep(HandoverRetryDelay);
        }
    }

    public static void Listen()
    {
        if (_listener is not { } listener) {
            Log.LogWarning("XDG_RUNTIME_DIR isn't set: the app isn't single-instance and gets no links");
            return;
        }

        var stopToken = StopTokenSource.Token;
        _ = BackgroundTask.Run(
            () => AcceptActivations(listener, stopToken),
            Log, "AcceptActivations failed",
            stopToken);
    }

    public static void Stop()
    {
        if (_listener is not { } listener)
            return;

        StopTokenSource.Cancel();
        listener.Dispose();
        // The socket goes first: a launch that finds the lock free must not find this socket too.
        // The lock file stays - deleting it would let two launches lock two different files.
        File.Delete(SocketPath);
        _lock?.Dispose();
    }

    // Private methods

    private static bool TryLock()
    {
        try {
            // FileShare.None is an exclusive flock, which the kernel drops if the process dies
            _lock = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException) {
            return false;
        }
    }

    private static bool TryForward(string url)
    {
        using var socket = NewSocket();
        try {
            socket.Connect(new UnixDomainSocketEndPoint(SocketPath));
            socket.Send(Encoding.UTF8.GetBytes(url));
            return true;
        }
        catch (SocketException) {
            return false;
        }
    }

    private static async Task AcceptActivations(Socket listener, CancellationToken cancellationToken)
    {
        while (true)
            try {
                using var connection = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
                var url = await ReadUrl(connection, cancellationToken).ConfigureAwait(false);
                OnActivated(url);
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested) {
                Log.LogError(e, "Failed to accept an activation");
                await Task.Delay(AcceptRetryDelay, cancellationToken).ConfigureAwait(false);
            }
    }

    private static async Task<string> ReadUrl(Socket connection, CancellationToken cancellationToken)
    {
        // The sender closes the connection after the link, so the end of the stream ends the link.
        // The timeout keeps a sender that never does from blocking the launches behind it.
        using var timeoutSource = cancellationToken.CreateLinkedTokenSource();
        timeoutSource.CancelAfter(ReceiveTimeout);
        using var reader = new StreamReader(new NetworkStream(connection), Encoding.UTF8);
        return await reader.ReadToEndAsync(timeoutSource.Token).ConfigureAwait(false);
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
}
