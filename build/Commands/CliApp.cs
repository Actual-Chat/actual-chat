using System.Text;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Help;
using Spectre.Console.Rendering;

namespace Build.Commands;

/// <summary>
/// The <c>b</c> command line: a git-style command tree on top of the Bullseye
/// target graph in <see cref="Program"/>.
/// </summary>
public static class CliApp
{
    private static readonly string[] HelpAliases = ["-?", "/?", "/h", "/help"];

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var context = new CliContext();
        var (app, helpProvider) = CreateApp(context);
        var model = helpProvider.Capture(app);
        TreeCommand.Model = model;
        if (args.Length != 0)
            return await app.RunAsync(NormalizeHelpArgs(args)).ConfigureAwait(false);

        var browser = new CommandBrowser(x => app.RunAsync(x), model, context);
        return await browser.Run(CancellationToken.None).ConfigureAwait(false);
    }

    // Private methods

    private static string[] NormalizeHelpArgs(string[] args)
        => [..args.Select(x => HelpAliases.Contains(x, StringComparer.OrdinalIgnoreCase) ? "--help" : x)];

    private static (CommandApp<TargetsCommand> App, ModelCapturingHelpProvider HelpProvider) CreateApp(
        CliContext context)
    {
        ModelCapturingHelpProvider? helpProvider = null;
        var registrar = new TypeRegistrar();
        registrar.RegisterInstance(typeof(CliContext), context);
        var app = new CommandApp<TargetsCommand>(registrar);
        app.Configure(c => {
            c.SetApplicationName("b");
            c.CaseSensitivity(CaseSensitivity.None);
            helpProvider = new ModelCapturingHelpProvider(new HelpProvider(c.Settings));
            c.SetHelpProvider(helpProvider);
            c.AddBranch("app", b => {
                b.SetDescription("Build & run the Voxt client apps (MAUI)");
                b.AddCommand<AppRunCommand>("run")
                    .WithDescription("Build the app for a platform, then install & launch it")
                    .WithExample("app", "run", "android")
                    .WithExample("app", "run", "android", "--release", "--prod")
                    .WithExample("app", "run", "ios", "--simulator")
                    .WithExample("app", "run", "windows", "--release", "--aot");
                b.AddCommand<AppBuildCommand>("build")
                    .WithDescription("Build (or publish) the app for a platform; add --launch to run it")
                    .WithExample("app", "build", "android", "--release");
                b.AddCommand<AppPackCommand>("pack")
                    .WithDescription("Build the store package (App Store / Play Store / MS Store)")
                    .WithExample("app", "pack", "android", "--prod");
            });
            c.AddCommand<TreeCommand>("tree")
                .WithDescription("Print the whole command tree");
            c.AddBranch("server", b => {
                b.SetDescription("Build & run the Voxt server");
                b.AddCommand<ServerLoopCommand>("loop")
                    .WithDescription("Run server-loop.ps1 - the npm build / dotnet build / server run loop");
            });
        });

        return (app, helpProvider!);
    }

    // Nested types

    // Spectre builds the command model internally and only hands it to the help
    // provider, so this captures it for CommandBrowser to render its menus from.
    private sealed class ModelCapturingHelpProvider(HelpProvider inner) : IHelpProvider
    {
        private bool _isCapturing;
        private ICommandModel? _model;
        private HelpProvider Inner { get; } = inner;

        public ICommandModel Capture(ICommandApp app)
        {
            if (_model is not null)
                return _model;

            _isCapturing = true;
            try {
                app.Run(["--help"]);
            }
            finally {
                _isCapturing = false;
            }

            return _model ?? throw new WithoutStackException("Failed to capture the command model.");
        }

        public IEnumerable<IRenderable> Write(ICommandModel model, ICommandInfo? command)
        {
            _model = model;
            return _isCapturing ? [] : Inner.Write(model, command);
        }
    }
}
