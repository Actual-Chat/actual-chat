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
            ThreadPool.SetMaxThreads(16384, 16384);

            var sMinIO = (Environment.GetEnvironmentVariable("DOTNET_THREADPOOL_MIN_IO") ?? "").Trim();
            var sMinWorker = (Environment.GetEnvironmentVariable("DOTNET_THREADPOOL_MIN_WORKER") ?? "").Trim();
            if (!int.TryParse(sMinIO, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minIO))
                minIO = 128;
            if (!int.TryParse(sMinWorker, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minWorker))
                minWorker = 128;
            ThreadPool.GetMinThreads(out int currentMinWorker, out int currentMinIO);
            minIO = Math.Max(minIO, currentMinIO);
            minWorker = Math.Max(minWorker, currentMinIO);

            if ((minIO, minWorker) == (currentMinWorker, currentMinWorker))
                return;

            if (!ThreadPool.SetMinThreads(currentMinWorker, minIO))
                throw StandardError.Internal("Can't set min. thread count.");
        }

        static void AdjustGrpcCoreThreadPool()
        {
            var grpcThreadsEnv = (Environment.GetEnvironmentVariable("GRPC_CORE_THREADPOOL_SIZE") ?? "").Trim();
            if (!int.TryParse(grpcThreadsEnv, NumberStyles.Integer, CultureInfo.InvariantCulture, out int grpcThreads))
                grpcThreads = 64;

            GrpcEnvironment.SetThreadPoolSize(grpcThreads);
            GrpcEnvironment.SetCompletionQueueCount(grpcThreads);
            // true is dangerous: if user block in async code, this can easily lead to deadlocks
            GrpcEnvironment.SetHandlerInlining(false);
        }
    }
}
