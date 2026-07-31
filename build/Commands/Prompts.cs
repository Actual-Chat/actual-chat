using Spectre.Console;
using Spectre.Console.Rendering;

namespace Build.Commands;

/// <summary>
/// Prompts with two things Spectre doesn't offer: a cursor that starts below the
/// first entry, and single-key shortcuts that return without Enter.
/// </summary>
internal static class Prompts
{
    public const string BackShortcut = "back";
    public const string RunShortcut = "run";
    public const string QuitShortcut = "quit";
    public static string Arrow => AnsiConsole.Profile.Capabilities.Unicode ? "←" : "<-";
    public static string BackLabel => $"[grey]{Arrow} back[/] [grey35](b)[/]";

    public static string? BackKeys(ConsoleKeyInfo key)
        => key.Key switch {
            ConsoleKey.Escape => QuitShortcut,
            ConsoleKey.LeftArrow => BackShortcut,
            _ => char.ToLowerInvariant(key.KeyChar) == 'b' ? BackShortcut : null,
        };

    public static string? BackOrRunKeys(ConsoleKeyInfo key)
        => char.ToLowerInvariant(key.KeyChar) == 'r' ? RunShortcut : BackKeys(key);

    public static string? QuitKeys(ConsoleKeyInfo key)
        => key.Key == ConsoleKey.Escape ? QuitShortcut : null;

    public static (T? Value, string? Shortcut) Show<T>(SelectionPrompt<T> prompt, Func<ConsoleKeyInfo, string?> shortcuts)
        where T : class
        // The first read returns a synthetic Down, so the cursor starts under the
        // "back" entry that heads every list.
        => Show(prompt.Show, shortcuts, true);

    public static (string? Value, string? Shortcut) ShowText(TextPrompt<string> prompt)
        => Show(prompt.Show, QuitKeys, false);

    // Private methods

    private static (T? Value, string? Shortcut) Show<T>(
        Func<IAnsiConsole, T> show,
        Func<ConsoleKeyInfo, string?> shortcuts,
        bool mustSkipFirst)
        where T : class
    {
        var console = new HookedConsole(AnsiConsole.Console, shortcuts, mustSkipFirst);
        try {
            return (show(console), null);
        }
        catch (ShortcutSignal e) {
            return (null, e.Shortcut);
        }
    }

    // Nested types

    private sealed class ShortcutSignal(string shortcut) : Exception
    {
        public string Shortcut { get; } = shortcut;
    }

    private sealed class HookedConsole : IAnsiConsole
    {
        private IAnsiConsole Inner { get; }
        public Profile Profile => Inner.Profile;
        public IAnsiConsoleCursor Cursor => Inner.Cursor;
        public IAnsiConsoleInput Input { get; }
        public IExclusivityMode ExclusivityMode => Inner.ExclusivityMode;
        public RenderPipeline Pipeline => Inner.Pipeline;

        public HookedConsole(IAnsiConsole inner, Func<ConsoleKeyInfo, string?> shortcuts, bool mustSkipFirst)
        {
            Inner = inner;
            Input = new HookedInput(inner.Input, shortcuts, mustSkipFirst);
        }

        public void Clear(bool home) => Inner.Clear(home);
        public void Write(IRenderable renderable) => Inner.Write(renderable);
        public void WriteAnsi(Action<AnsiWriter> write) => Inner.WriteAnsi(write);
    }

    private sealed class HookedInput(
        IAnsiConsoleInput inner,
        Func<ConsoleKeyInfo, string?> shortcuts,
        bool mustSkipFirst) : IAnsiConsoleInput
    {
        private static readonly ConsoleKeyInfo Down = new('\0', ConsoleKey.DownArrow, false, false, false);
        private IAnsiConsoleInput Inner { get; } = inner;
        private Func<ConsoleKeyInfo, string?> Shortcuts { get; } = shortcuts;
        private bool _isStarted = !mustSkipFirst;
        public bool IsKeyAvailable() => !_isStarted || Inner.IsKeyAvailable();

        public ConsoleKeyInfo? ReadKey(bool intercept)
            => TryTakeFirst() ?? Check(Inner.ReadKey(intercept));

        public async Task<ConsoleKeyInfo?> ReadKeyAsync(bool intercept, CancellationToken cancellationToken)
            => TryTakeFirst()
                ?? Check(await Inner.ReadKeyAsync(intercept, cancellationToken).ConfigureAwait(false));

        // Private methods

        private ConsoleKeyInfo? TryTakeFirst()
        {
            if (_isStarted)
                return null;

            _isStarted = true;
            return Down;
        }

        private ConsoleKeyInfo? Check(ConsoleKeyInfo? key)
        {
            if (key is { } x && Shortcuts(x) is { } shortcut)
                throw new ShortcutSignal(shortcut);

            return key;
        }
    }
}
