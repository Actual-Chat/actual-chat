using System.Text;
using ActualChat.Audio.WebM;
using ActualLab.Fusion.Client;
using ActualLab.Rpc;
using Grpc.Core;

namespace ActualChat.App.Server;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        Tracer.Default =
#if DEBUG
            new Tracer("Server", x => Console.WriteLine("@ " + x.Format()));
#else
            Tracer.None;
#endif

        RpcDefaults.Mode = RpcMode.Server;
        RpcDefaultDelegates.HashProvider = data => {
            // SIMD-based version of Blake3 we use here is much faster than SSH256.
            // See https://github.com/xoofx/Blake3.NET?tab=readme-ov-file#results
            var hash = Blake3.Hasher.Hash(data.Data.Span);
            return Convert.ToBase64String(hash.AsSpan()[..18]); // 18 bytes -> 24 chars
        };
        FusionDefaults.Mode = FusionMode.Server;
        RemoteComputedSynchronizer.Default = null!; // Server shouldn't use it

        Console.OutputEncoding = Encoding.UTF8;
        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        Activity.ForceDefaultIdFormat = true;
        // TODO(AK): Try to disable HTTP/3 for Google Speech-To-Text only rather than globally
        AppContext.SetSwitch("System.Net.SocketsHttpHandler.Http3Support", false);
        CommandLineHandler.Process(args);
        AdjustThreadPool();
        AdjustGrpcCoreThreadPool();

        using var appHost = new AppHost();
        try {
            appHost.Build();
        }
        catch (Exception ex) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Can't build application host. Exception: {ex}");
            Console.ResetColor();
        }

        Constants.HostInfo = appHost.Services.HostInfo();
        StaticLog.Factory = appHost.Services.LoggerFactory();
        if (Constants.DebugMode.WebMReader)
            WebMReader.DebugLog = appHost.Services.LogFor(typeof(WebMReader));
        if (Constants.DebugMode.Npgsql)
            Npgsql.NpgsqlLoggingConfiguration.InitializeLogging(appHost.Services.GetRequiredService<ILoggerFactory>(),true);

        await appHost.InvokeInitializers().ConfigureAwait(false);
        await appHost.Run().ConfigureAwait(false);

        // We preserve default thread pool settings only if they are bigger of our minimals
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
            // true is dangerous: if user block in async code, this can easily lead to deadlocks
            GrpcEnvironment.SetHandlerInlining(false);
        }
    }
}
