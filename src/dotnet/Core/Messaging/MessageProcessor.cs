using ActualLab.Diagnostics;

namespace ActualChat.Messaging;

public interface IMessageProcessor<TMessage> : IAsyncDisposable
    where TMessage : class
{
    IMessageProcess<TMessage> Enqueue(TMessage message, CancellationToken cancellationToken = default);
    Task Complete(CancellationToken cancellationToken = default);

    public IMessageProcess<TSpecific> Enqueue<TSpecific>(TSpecific message, CancellationToken cancellationToken = default)
        where TSpecific : TMessage
        => (IMessageProcess<TSpecific>)Enqueue((TMessage)message, cancellationToken);
}

public abstract class MessageProcessorBase<TMessage>(CancellationTokenSource? stopTokenSource = null)
    : WorkerBase(stopTokenSource), IMessageProcessor<TMessage>
    where TMessage : class
{
    protected Channel<IMessageProcess<TMessage>>? Queue { get; set; }

    private ILogger? _log;

    protected static bool DebugMode => Constants.DebugMode.MessageProcessor;
    protected ILogger Log => _log ??= StaticLog.For(GetType());
    protected ILogger? DebugLog => DebugMode ? Log.IfEnabled(LogLevel.Debug) : null;

    public int QueueSize { get; init; } = Constants.MessageProcessing.QueueSize;
    public TimeSpan ProcessCallTimeout { get; init; } = Constants.MessageProcessing.ProcessCallTimeout;
    public BoundedChannelFullMode QueueFullMode { get; init; } = BoundedChannelFullMode.Wait;

    protected override Task DisposeAsyncCore()
    {
        Queue?.Writer.TryComplete();
        return WhenRunning ?? Task.CompletedTask;
    }

    public IMessageProcess<TSpecific> Enqueue<TSpecific>(TSpecific message, CancellationToken cancellationToken = default)
        where TSpecific : TMessage
        => (IMessageProcess<TSpecific>)Enqueue((TMessage)message, cancellationToken);

    public IMessageProcess<TMessage> Enqueue(TMessage message, CancellationToken cancellationToken = default)
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        this.Start();
        var process = (IMessageProcess<TMessage>)MessageProcess.New(message, cancellationToken);
        try {
            var queueTask = Queue!.Writer.WriteAsync(process, cancellationToken);
            if (!queueTask.IsCompletedSuccessfully)
                _ = queueTask.AsTask().ContinueWith(async queueTask1 => {
                    try {
                        await queueTask1.ConfigureAwait(false);
                    }
                    catch (Exception e) {
                        process.MarkFailed(e);
                    }
                }, TaskScheduler.Default);
        }
        catch (Exception e) {
            process.MarkFailed(e);
        }
        return process;
    }

    public Task Complete(CancellationToken cancellationToken = default)
    {
        this.Start();
        Queue!.Writer.TryComplete();
        return WhenRunning == null ? Task.CompletedTask : WhenRunning.WaitAsync(cancellationToken);
    }

    protected abstract Task<object?> Process(TMessage message, CancellationToken cancellationToken);

    protected override Task OnStart(CancellationToken cancellationToken)
    {
        if (Queue != null!)
            return Task.CompletedTask;

        Queue = Channel.CreateBounded<IMessageProcess<TMessage>>(
            new BoundedChannelOptions(QueueSize) {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false, // Enqueue anyway runs a new task for any WriteAsync
                FullMode = QueueFullMode,
            });
        return Task.CompletedTask;
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var queuedProcesses = Queue!.Reader.ReadAllAsync(cancellationToken);
        await foreach (var process in queuedProcesses.ConfigureAwait(false)) {
            DebugLog?.LogDebug($"{nameof(OnRun)} cycle");
            var message = process.Message;
            process.MarkStarted();
            Task<object?>? processTask = null;
            try {
                process.CancellationToken.ThrowIfCancellationRequested();
                object? result;
                using (var cts = process.CancellationToken.CreateLinkedTokenSource()) {
                    cts.CancelAfter(ProcessCallTimeout);
                    processTask = Process(message, cts.Token);
                    result = await processTask.ConfigureAwait(false);
                }
                if (result is Task<object?> resultTask) {
                    // Special case: Process may return Task<Task<object?>>,
                    // in this case we assume the rest of the processing will
                    // be done later, but the processor can move on to the
                    // next message.
                    if (IsTerminator(message)) {
                        result = await resultTask.ConfigureAwait(false);
                        process.MarkCompleted(result);
                        break;
                    }
                    process.MarkCompletedAfter(resultTask); // Notice we don't await here!
                }
                else {
                    process.MarkCompleted(result);
                    if (IsTerminator(message))
                        break;
                }
            }
            catch (TimeoutException) {
                if (processTask != null)
                    process.MarkCompletedAfter(processTask); // Notice we don't await here!

                if (IsTerminator(message))
                    break;
            }
            catch (Exception e) {
                process.MarkFailed(e);
                if (IsTerminator(message))
                    break;
            }
        }

        bool IsTerminator(object message)
        {
            if (message is not (ITerminatorMessage or IMaybeTerminatorMessage { IsTerminator: true }))
                return false;

            Queue.Writer.TryComplete();
            return true;
        }
    }
}

public sealed class MessageProcessor<TMessage> : MessageProcessorBase<TMessage>
    where TMessage : class
{
    private Func<TMessage, CancellationToken, Task<object?>> Processor { get; }

    public MessageProcessor(
        Func<TMessage, CancellationToken, Task<object?>> processor,
        CancellationTokenSource? stopTokenSource = null)
        : base(stopTokenSource)
        => Processor = processor;

    protected override Task<object?> Process(TMessage message, CancellationToken cancellationToken)
        => Processor.Invoke(message, cancellationToken);
}
