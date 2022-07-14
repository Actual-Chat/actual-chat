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
            var envMinIo = Environment.GetEnvironmentVariable("DOTNET_THREADPOOL_MIN_IO");
            if (string.IsNullOrWhiteSpace(envMinIo)
                || !int.TryParse(envMinIo, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minIoThreads)) {
                minIoThreads = 8;
            }
            var envMinWorker = Environment.GetEnvironmentVariable("DOTNET_THREADPOOL_MIN_WORKER");
            if (string.IsNullOrWhiteSpace(envMinWorker)
                || !int.TryParse(envMinWorker, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minWorkerThreads)) {
                minWorkerThreads = 8;
            }
            ThreadPool.GetMinThreads(out int currentMinWorker, out int currentMinIo);
            if (currentMinIo < minIoThreads) {
                if (!ThreadPool.SetMinThreads(currentMinWorker, minIoThreads)) {
                    throw new Exception("ERROR: Can't set min IO threads.");
                }
                currentMinIo = minIoThreads;
            }
            if (currentMinWorker < minWorkerThreads && !ThreadPool.SetMinThreads(minWorkerThreads, currentMinIo)) {
                throw new Exception("ERROR: Can't set min worker threads.");
            }
            ThreadPool.SetMaxThreads(4096, 4096);
        }

        static void AdjustGrpcCoreThreadPool()
        {
            var grpcThreadsEnv = Environment.GetEnvironmentVariable("GRPC_CORE_THREADPOOL_SIZE");
            if (string.IsNullOrWhiteSpace(grpcThreadsEnv)
                || !int.TryParse(grpcThreadsEnv, NumberStyles.Integer, CultureInfo.InvariantCulture, out int grpcThreads)) {
                grpcThreads = 8;
            }
            GrpcEnvironment.SetThreadPoolSize(grpcThreads);
            GrpcEnvironment.SetCompletionQueueCount(grpcThreads);
            // requires user to never block in async code, can easily lead to deadlocks
            GrpcEnvironment.SetHandlerInlining(true);
        }
    }
}
