using Windows.UI.StartScreen;

namespace ActualChat.App.Maui;

public static class JumpListManager
{
    public const string QuitArgs = "--quit";

    public static async Task PopulateJumpList()
    {
        var jumpList = await JumpList.LoadCurrentAsync();
        var count = jumpList.Items.Count;
        // NOTE(AY): "x.svg" icon doesn't work (nothing is showing up), but the .png version is displayed
        // in a wrong color, if your Windows theme is dark. So let's keep it as-is for now.
        AddJumpListItem(jumpList, QuitArgs, "Quit Actual Chat", "ms-appx:///Platforms/Windows/Assets/x.svg");
        if (jumpList.Items.Count == count)
            return;

        await jumpList.SaveAsync();
    }

    public static async Task ClearJumpList()
    {
        var jumpList = await JumpList.LoadCurrentAsync();
        var count = jumpList.Items.Count;
        RemoveJumpListItem(jumpList, QuitArgs);
        if (jumpList.Items.Count == count)
            return;

        await jumpList.SaveAsync();
    }

    private static void AddJumpListItem(JumpList jumpList, string args, string displayName, string icon = "")
    {
        if (jumpList.Items.Any(c => OrdinalEquals(c.Arguments, args)))
            return;

        var item = JumpListItem.CreateWithArguments(args, displayName);
        item.Logo = icon.IsNullOrEmpty() ? null : new Uri(icon);
        jumpList.Items.Add(item);
    }

    private static void RemoveJumpListItem(JumpList jumpList, string args)
    {
        var quitItem = jumpList.Items.FirstOrDefault(c => OrdinalEquals(c.Arguments, args));
        if (quitItem == null)
            return;

        jumpList.Items.Remove(quitItem);
    }
}
