using AppKit;

namespace ActualChat.App.Maui;

public static class Program
{
    // This is the main entry point of the application.
    public static void Main(string[] args)
    {
        NSApplication.Init();
        MacOSEssentialsDefaults.Apply();
        NSApplication.SharedApplication.Delegate = new AppDelegate();
        NSApplication.Main(args);
    }
}
