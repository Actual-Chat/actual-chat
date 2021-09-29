using CliWrap;
using static Crayon.Output;

namespace Build;

internal static class CliWrapCommandExtensions
{
    private static readonly Stream _stdout = Console.OpenStandardOutput();
    private static readonly Stream _stderr = Console.OpenStandardError();

    internal static Command ToConsole(this Command command) => command | (_stdout, _stderr);
    internal static Command ToConsole(this Command command, string prefix) => command | (s => Console.WriteLine(prefix + s), s => Console.Error.WriteLine(prefix + Red(s)));
}


