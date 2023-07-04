using System;
using System.IO;
using CliWrap;
using static Crayon.Output;

namespace Build
{

    internal static class CliWrapCommandExtensions
    {
        private static readonly Stream _stdout = Console.OpenStandardOutput();
        private static readonly Stream _stderr = Console.OpenStandardError();

        internal static Command ToConsole(this Command command) => command | (_stdout, _stderr);
        internal static Command ToConsole(this Command command, string prefix) => command | (s => Console.WriteLine(prefix + Colorize(s)), s => Console.Error.WriteLine(prefix + Red(s)));

        public static Command Npm()
            => Cli.Wrap(Utils.FindNpmExe())
                .WithWorkingDirectory(Path.Combine("src", "nodejs"))
                .WithEnvironmentVariables(new Dictionary<string, string?>(1) { ["CI"] = "true" });

        public static Command WithArguments(this Command command, params string[] args)
            => command.WithArguments(args, false);

        /// <summary>
        /// Stdout redirect won't use colors from the original output, we should use pseudo console to correct redirect colors.
        /// <see href="https://github.com/microsoft/terminal/blob/main/samples/ConPTY/MiniTerm/MiniTerm/Program.cs#L16" />
        /// For now we don't do this and use the simplest solution.
        /// </summary>
        public static string Colorize(string str)
        {
            if (str.Contains("error", StringComparison.OrdinalIgnoreCase))
                return Red(str);

            if (str.Contains("Exception:", StringComparison.OrdinalIgnoreCase))
                return Red(str);

            if (str.Contains("fail:", StringComparison.OrdinalIgnoreCase))
                return Red(str);

            if (str.Contains(" warning ", StringComparison.OrdinalIgnoreCase))
                return Yellow(str);

            if (str.Contains("watch :", StringComparison.OrdinalIgnoreCase))
                return Underline(Magenta(str));

            return str;
        }
    }
}
