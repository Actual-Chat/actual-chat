using System.Text;
using ActualChat.Audio.WebM;
using ActualChat.Module;
using ActualLab.Rpc;
using Grpc.Core;

namespace ActualChat.App.Server;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Tracer.Default =
#if DEBUG
            new Tracer("Server", x => Console.WriteLine("@ " + x.Format()));
#else
            Tracer.None;
#endif

        RuntimeInfo.IsServer = true;
        ApiContractsModuleInitializer.Load();
        CoreModuleInitializer.Initialize();

        RpcOutboundCallOptions.Default = RpcOutboundCallOptions.Default with {
            Hasher = data => {
                // SIMD-based version of Blake3 we use here is much faster than SSH256.
                // See https://github.com/xoofx/Blake3.NET?tab=readme-ov-file#results
                var hash = Blake3.Hasher.Hash(data.Span);
                return Convert.ToBase64String(hash.AsSpan()[..18]); // 18 bytes -> 24 chars
            },
        };
        ComputedSynchronizer.Default = ComputedSynchronizer.None.Instance; // Server shouldn't use it

        Console.OutputEncoding = Encoding.UTF8;
        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        Activity.ForceDefaultIdFormat = true;
        // TODO(AK): Try to disable HTTP/3 for Google Speech-To-Text only rather than globally
        AppContext.SetSwitch("System.Net.SocketsHttpHandler.Http3Support", false);
        CommandLineHandler.Process(args);
        AdjustThreadPool();
        AdjustGrpcCoreThreadPool();

        var appHost = new AppHost();
        await using var _ = appHost.ConfigureAwait(false);
        try {
            appHost.Build();
        }
        catch (Exception ex) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Can't build application host. Exception: {ex}");
            Console.ResetColor();
            return 1;
        }

        var c = appHost.Services;
        Constants.HostInfo = c.HostInfo();
        StaticLog.Factory = c.LoggerFactory();
        if (Constants.DebugMode.WebMReader)
            WebMReader.DebugLog = c.LogFor(typeof(WebMReader));
        if (Constants.DebugMode.Npgsql) {
            var loggerFactory = c.GetRequiredService<ILoggerFactory>();
            Npgsql.NpgsqlLoggingConfiguration.InitializeLogging(loggerFactory, parameterLoggingEnabled: true);
        }
        CommandLineHandler.AppHost = appHost;

        // Exit-code contract:
        //   0 - stop-style termination (StopApplication / Ctrl+C / SIGTERM / 's' key / /health/stop / debugUI.stopServer)
        //   non-zero - start failure (exception during initializers or host startup)
        try {
            await appHost.RunInitializers().ConfigureAwait(false);
            await appHost.Run().ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Server failed to start. Exception: {ex}");
            Console.ResetColor();
            return 2;
        }

        // We preserve default thread pool settings only if they are higher than our minimums
        static void AdjustThreadPool()
        {
            var (maxWorker, maxIO) = (16384, 16384);
            ThreadPool.SetMaxThreads(maxWorker, maxIO);

            var sMinWorker = (Environment.GetEnvironmentVariable("DOTNET_THREADPOOL_MIN_WORKER") ?? "").Trim();
            var sMinIO = (Environment.GetEnvironmentVariable("DOTNET_THREADPOOL_MIN_IO") ?? "").Trim();
            if (!NumberExt.TryParsePositiveInt(sMinWorker, out int minWorker))
                minWorker = HardwareInfo.GetProcessorCountFactor(4);
            if (!NumberExt.TryParsePositiveInt(sMinIO, out int minIO))
                minIO = HardwareInfo.GetProcessorCountFactor(4);
            ThreadPool.GetMinThreads(out int currentMinWorker, out int currentMinIO);
            minIO = Math.Max(minIO, currentMinIO);
            minWorker = Math.Max(minWorker, currentMinIO);

            if ((minIO, minWorker) == (currentMinWorker, currentMinWorker))
                return;

            Console.WriteLine($"Thread pool thread size: {minWorker}..{maxWorker} worker, {minIO}..{maxIO} IO threads");
            if (!ThreadPool.SetMinThreads(minWorker, minIO))
                throw StandardError.Internal("Can't set min. thread count.");
        }

        static void AdjustGrpcCoreThreadPool()
        {
            var threadCountEnv = (Environment.GetEnvironmentVariable("GRPC_CORE_THREADPOOL_SIZE") ?? "").Trim();
            if (!NumberExt.TryParsePositiveInt(threadCountEnv, out int threadCount))
                threadCount = HardwareInfo.GetProcessorCountFactor(4);

            Console.WriteLine($"GRPC thread pool size: {threadCount}");
            GrpcEnvironment.SetThreadPoolSize(threadCount);
            GrpcEnvironment.SetCompletionQueueCount(threadCount);
            // true is dangerous: if the code blocks in async code, this can lead to deadlocks
            GrpcEnvironment.SetHandlerInlining(false);
        }
    }
}
