using Windows.UI.StartScreen;

namespace ActualChat.App.Maui;

public static class JumpListManager
{
    public const string QuitArgs = "--quit";

    public static async Task PopulateJumpList()
    {
        var jumpList = await JumpList.LoadCurrentAsync();
        var count = jumpList.Items.Count;
        AddJumpListItem(jumpList, QuitArgs, "Quit Actual Chat", "ms-appx:///Platforms/Windows/Assets/jump_item_quit.png");
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

    private static void AddJumpListItem(JumpList jumpList, string args, string displayName, string logo)
    {
        if (jumpList.Items.Any(c => OrdinalEquals(c.Arguments, args)))
            return;
        var item = JumpListItem.CreateWithArguments(args, displayName);
        if (!string.IsNullOrEmpty(logo))
            item.Logo = new Uri(logo);
        else
            item.Logo = null;
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
