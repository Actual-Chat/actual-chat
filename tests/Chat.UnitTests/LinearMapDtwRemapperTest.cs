using ActualChat.Transcription;

namespace ActualChat.Chat.UnitTests;

public class LinearMapDtwRemapperTest
{
    [Fact]
    public void BasicTest()
    {
        var oldTimeMap = new LinearMap(0, 1.55f, 7, 2.19f, 9, 2.51f, 25, 3.31f, 33, 4.0299997f, 43, 4.43f, 52, 5.0699997f, 56, 5.91f, 57, 5.99f, 61, 6.71f, 66, 6.87f, 67, 7.1899996f, 74, 7.8f, 75, 7.96f, 96, 9.08f, 105, 9.8f, 112, 10.54f, 113, 10.94f, 118, 11.26f, 125, 12.059999f, 165, 14.52f, 166, 14.52f, 175, 15, 211, 17.32f, 218, 18.02f, 219, 18.34f, 227, 18.82f, 230, 19.51f, 231, 19.67f, 253, 20.95f, 268, 22.35f);
        var oldText = "Blazor is a modern front end web framework based on HTML CSS, and C Sharp. That helps you build web apps faster. With Blazor, build web apps using reusable component that can be run from both the client and the server. So that the you can deliver great web experience.";
        var newText = "Blazor is a modern front-end web framework based on HTML, CSS, and C# that helps you build web apps faster. With Blazor, build web apps using reusable components that can be run from both the client and the server, so that you can deliver a great web experience.";
        var newTimeMap = LinearMapDtwRemapper.Remap(oldText, newText, oldTimeMap);
        newTimeMap.IsDegenerate.Should().BeFalse();
    }
}
