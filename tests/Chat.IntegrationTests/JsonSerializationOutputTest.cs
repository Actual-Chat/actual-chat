using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.IntegrationTests;

public class JsonSerializationOutputTest : TestBase
{
    public JsonSerializationOutputTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public void DumpWarmupJson()
    {
        Dump(new UserLanguageSettings() { Primary = Languages.French });
        Dump(ApiArray.New(new ActiveChat(ChatId.ParseOrNone("dpwo1tm0tw"))));
        Dump(new UserOnboardingSettings() { IsAvatarStepCompleted = true });
        Dump(new ChatListSettings(ChatListOrder.ByAlphabet));
        Dump(new UserBubbleSettings() { ReadBubbles = ApiArray.New("x") });
    }

    private void Dump<T>(T instance)
    {
        var s = SystemJsonSerializer.Default;
        Out.WriteLine($"{typeof(T).GetName()}:");
        Out.WriteLine("\"" + s.Write(instance).Replace("\"", "\\\"", StringComparison.OrdinalIgnoreCase) + "\"");
        Out.WriteLine("");
    }
}
