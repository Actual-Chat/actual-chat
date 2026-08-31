using ActualLab.Caching;
using CoreFoundation;

namespace ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;

public abstract class ComputedStateView(IosHub hub) : StatefulView(hub)
{
    private static readonly ConcurrentDictionary<Type, string> StateCategoryCache
        = new(HardwareInfo.ProcessorCountPo2, 131);
    private static readonly ConcurrentDictionary<Type, IComputedStateOptions> StateOptionsCache
        = new(HardwareInfo.ProcessorCountPo2, 131);
    private static readonly ConcurrentDictionary<Type, Func<Type, IComputedStateOptions>> CreateDefaultStateOptionsCache
        = new(HardwareInfo.ProcessorCountPo2, 131);

    public static Func<Type, IComputedStateOptions> DefaultStateOptionsFactory { get; set; } = CreateDefaultStateOptions;

    public static ComputedState<T>.Options GetStateOptions<T>(
        Type componentType, Func<Type, ComputedState<T>.Options>? optionsFactory = null)
        => (ComputedState<T>.Options)StateOptionsCache.GetOrAdd(componentType,
            optionsFactory as Func<Type, IComputedStateOptions> ?? DefaultStateOptionsFactory);

    public static IComputedStateOptions GetStateOptions(
        Type componentType, Func<Type, IComputedStateOptions>? optionsFactory = null)
        => StateOptionsCache.GetOrAdd(componentType, optionsFactory ?? DefaultStateOptionsFactory);

    public static string GetStateCategory(Type componentType)
        => StateCategoryCache.GetOrAdd(componentType, static t => $"{t.GetName()}.State");

    public static string GetMutableStateCategory(Type componentType)
        => StateCategoryCache.GetOrAdd(componentType, static t => $"{t.GetName()}.MutableState");

    public static IComputedStateOptions CreateDefaultStateOptions(Type componentType)
        => CreateDefaultStateOptionsCache.GetOrAdd(componentType,
            static componentType => {
                var type = componentType;
                while (type is not null) {
                    if (type.IsGenericType
                        && type.GetGenericTypeDefinition() is var gtd
                        && gtd == typeof(ComputedStateView<>)) {
                        var stateType = type.GetGenericArguments().Single();
                        return GenericInstanceCache
                            .Get<Func<Type, IComputedStateOptions>>(typeof(CreateDefaultStateOptionsFactory<>), stateType)
                            .Invoke(componentType);
                    }
                    type = type.BaseType;
                }
                throw new ArgumentOutOfRangeException(nameof(componentType));
            }).Invoke(componentType);

    // Nested types

    public sealed class CreateDefaultStateOptionsFactory<T> : GenericInstanceFactory, IGenericInstanceFactory<T>
    {
        public override object Generate()
            => (Type componentType) => (IComputedStateOptions)new ComputedState<T>.Options() {
                Category = GetStateCategory(componentType),
            };
    }
}

public abstract class ComputedStateView<T>(IosHub hub) : ComputedStateView(hub), IStatefulView<T>
{
    private bool _isInitiallyRendered;
    protected State UntypedState => base.State;
    protected new ComputedState<T> State => (ComputedState<T>)base.State;
    IState<T> IStatefulView<T>.State => (IState<T>)base.State;

    internal Func<CancellationToken, Task<T>> StateComputer => field ??= ComputeState;

    protected virtual ComputedState<T>.Options GetStateOptions()
        => GetStateOptions<T>(GetType());

    protected override (State State, object? StateInitializeOptions) CreateState()
    {
        // Synchronizes ComputeState call as per:
        // https://github.com/servicetitan/Stl.Fusion/issues/202
        var stateOptions = GetStateOptions();
        var state = ComputedStateViewState<T>.New(stateOptions, this, Services);
        return (state, stateOptions);
    }

    protected override void NotifyStateHasChanged()
    {
        var computed = State.Computed;
        if (computed.Error is { } error) {
            // State.Value would rethrow this into Fusion's event handler, where it's swallowed -
            // and the view would sit on whatever it last rendered with nothing logged here.
            Log.LogError(error, "Failed to compute state");
            return;
        }

        var model = computed.Value;
        // DispatchAsync, not MainThread.BeginInvokeOnMainThread: that runs inline on the main
        // thread, and the state starts in the base ctor - a sync computed would render mid-build.
        DispatchQueue.MainQueue.DispatchAsync(() => {
            try {
                if (!_isInitiallyRendered) {
                    OnInitialRender(model);
                    _isInitiallyRendered = true;
                }
                else
                    OnStateChanged(model);
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to render state changes");
            }
        });
    }

    // Runs on the first state update, not on construction - so a parent laying this view out must
    // clear its TranslatesAutoresizingMaskIntoConstraints itself rather than wait for this.
    protected abstract void OnInitialRender(T model);
    protected abstract void OnStateChanged(T model);
    protected abstract Task<T> ComputeState(CancellationToken cancellationToken);
}
