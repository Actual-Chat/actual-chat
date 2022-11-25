using Stl.Reflection;

namespace ActualChat.Commands;

public class EventHandlerResolver : IEventHandlerResolver
{
    protected ILogger Log { get; init; }
    protected ICommandHandlerRegistry Registry { get; }
    protected Func<CommandHandler, Type, bool> Filter { get; }
    protected ConcurrentDictionary<Type, IReadOnlyList<IReadOnlyList<CommandHandler>>> Cache { get; } = new();

    public EventHandlerResolver(
        ICommandHandlerRegistry registry,
        IEnumerable<CommandHandlerFilter>? filters = null,
        ILogger<EventHandlerResolver>? log = null)
    {
        Log = log ?? new NullLogger<EventHandlerResolver>();
        Registry = registry;
        var aFilters = filters?.ToArray() ?? Array.Empty<CommandHandlerFilter>();
        Filter = (commandHandler, type) => aFilters.All(f => f.IsCommandHandlerUsed(commandHandler, type));
    }

    public IReadOnlyList<IReadOnlyList<CommandHandler>> GetEventHandlers(Type commandType)
        => Cache.GetOrAdd(commandType, (commandType1, self) => {
            var baseTypes = commandType1.GetAllBaseTypes(true, true)
                .Select((type, index) => (Type: type, Index: index))
                .ToArray();
            var handlers = (
                from typeEntry in baseTypes
                from handler in self.Registry.Handlers
                where handler.CommandType == typeEntry.Type && self.Filter(handler, commandType1)
                orderby handler.Priority descending, typeEntry.Index descending
                select handler
                ).Distinct().ToList();
            var nonFilterHandlers = handlers
                .Where(h => !h.IsFilter)
                .ToList();
            var filterHandlers = handlers
                .Where(h => h.IsFilter)
                .ToList();
            if (!typeof(IEvent).IsAssignableFrom(commandType1) && nonFilterHandlers.Count > 1) {
                var exception = Stl.CommandR.Internal.Errors.MultipleNonFilterHandlers(commandType1);
                var message = $"Non-filter handlers: {handlers.ToDelimitedString()}";
                // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
                Log.LogCritical(exception, message);
                throw exception;
            }

            return nonFilterHandlers
                .Select(h => new List<CommandHandler>(filterHandlers.Append(h)).ToArray())
                .ToArray();
        }, this);
}
