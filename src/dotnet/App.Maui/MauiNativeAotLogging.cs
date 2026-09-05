namespace ActualChat.App.Maui;

/// <summary>
/// Stand-alone, file-based crash/first-chance-exception logger used for NativeAOT bring-up
/// debugging when the regular <see cref="ActualChat.UI.Blazor.App.FirstChanceExceptionLogger"/> /
/// MAUI logging pipeline isn't yet functional.
///
/// NOT called by default: call <see cref="Use"/> manually from <see cref="MauiProgram"/>'s
/// static constructor (as the very first statement) when investigating a crash that happens
/// before regular logging comes up.
///
/// <para>
/// WARNING: the implementation intentionally avoids any locking — each callback opens and
/// closes the log file via <see cref="File.AppendAllText(string, string)"/>. Under concurrent
/// FCEs this creates a positive-feedback loop: a file-lock <see cref="IOException"/> raised
/// by one append is itself a FirstChanceException, which triggers another append attempt, and
/// so on. Only enable while debugging; disable again before shipping.
/// </para>
/// </summary>
public static class MauiNativeAotLogging
{
    private const string CrashLogPath = @"C:\Temp\fce.log";

    public static void Use()
    {
        AppDomain.CurrentDomain.FirstChanceException += (_, e) => {
            try {
                var line = $"[{DateTime.Now:HH:mm:ss.fff}] FCE: {e.Exception}\n";
                File.AppendAllText(CrashLogPath, line);
            }
            catch { /* best effort */ }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => {
            try {
                var line = $"[{DateTime.Now:HH:mm:ss.fff}] UNHANDLED: {e.ExceptionObject}\n";
                File.AppendAllText(CrashLogPath, line);
            }
            catch { /* best effort */ }
        };

        try {
            File.WriteAllText(CrashLogPath, $"[{DateTime.Now:HH:mm:ss.fff}] === App starting ===\n");
        }
        catch { /* best effort */ }
    }

    /// <summary>Appends a single breadcrumb line to the crash log.</summary>
    public static void Log(string message)
    {
        try {
            File.AppendAllText(CrashLogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch { /* best effort */ }
    }
}
