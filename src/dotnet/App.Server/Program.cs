using System.Text;
using ActualChat.Audio.WebM;
using ActualChat.Hosting;
using Grpc.Core;

namespace ActualChat.App.Server;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        Activity.ForceDefaultIdFormat = true;
        // TODO(AK): try to disable Http/3 for google speech-to-text only instead of global toggle!
        AppContext.SetSwitch("System.Net.SocketsHttpHandler.Http3Support", false);
        AdjustThreadPool();
        AdjustGrpcCoreThreadPool();

        using var appHost = new AppHost();
        try {
            await appHost.Build().ConfigureAwait(false);
        }
        catch (Exception ex) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Can't build application host. Exception: {ex}");
            Console.ResetColor();
        }

        Constants.HostInfo = appHost.Services.GetRequiredService<HostInfo>();
        if (Constants.DebugMode.WebMReader)
            WebMReader.DebugLog = appHost.Services.LogFor(typeof(WebMReader));

        await appHost.Initialize().ConfigureAwait(false);
        await appHost.Run().ConfigureAwait(false);

        // we preserve default thread pool settings only if they are bigger of our minimals
        static void AdjustThreadPool()
        {
            var (maxWorker, maxIO) = (16384, 16384);
            ThreadPool.SetMaxThreads(maxWorker, maxIO);

            var sMinWorker = (Environment.GetEnvironmentVariable("DOTNET_THREADPOOL_MIN_WORKER") ?? "").Trim();
            var sMinIO = (Environment.GetEnvironmentVariable("DOTNET_THREADPOOL_MIN_IO") ?? "").Trim();
            if (!int.TryParse(sMinWorker, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minWorker))
                minWorker = HardwareInfo.GetProcessorCountFactor(4);
            if (!int.TryParse(sMinIO, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minIO))
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
            if (!int.TryParse(threadCountEnv, NumberStyles.Integer, CultureInfo.InvariantCulture, out int threadCount))
                threadCount = HardwareInfo.GetProcessorCountFactor(4);

            Console.WriteLine($"GRPC thread pool size: {threadCount}");
            GrpcEnvironment.SetThreadPoolSize(threadCount);
            GrpcEnvironment.SetCompletionQueueCount(threadCount);
            // true is dangerous: if user block in async code, this can easily lead to deadlocks
            GrpcEnvironment.SetHandlerInlining(false);
        }
    }
}
