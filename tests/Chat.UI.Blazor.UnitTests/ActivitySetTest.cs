using System.Collections.Immutable;
using ActualChat.UI.Blazor.Services;
using ActivityKind = ActualChat.UI.Blazor.Services.ActivityKind;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class ActivitySetTest
{
    private static readonly ChatId TestChatId = ChatId.Parse("aaaaaaaaaaaaaaaaaaaa");
    private static readonly ActivityChatInfo Chat = new (TestChatId, "Chat", "", 0);

    [Fact]
    public void ActivitiesAreOrderedByPriority()
    {
        // arrange
        var upload = new UploadActivity(1, 0, 100, ImmutableList<UploadActivityItem>.Empty);
        var location = new LocationActivity(Chat);
        var audio = new AudioActivity(ActivityKind.Listening, Chat, false);

        // act
        var set = new ActivitySet([upload, location, audio]);

        // assert
        set.Primary.Should().Be(audio);
        set.Activities.Select(x => x.Kind).Should()
            .Equal(ActivityKind.Listening, ActivityKind.SharingLocation, ActivityKind.Uploading);
    }

    [Fact]
    public void AudioKindsKeepTheirRelativeOrder()
    {
        // arrange
        var recording = new AudioActivity(ActivityKind.Recording, Chat, false);
        var armed = new AudioActivity(ActivityKind.Armed, Chat, false, false);

        // act & assert
        new ActivitySet([armed, recording]).Primary.Should().Be(recording);
    }

    [Fact]
    public void EqualityIsBySequence()
    {
        // arrange
        var a = new ActivitySet([new LocationActivity(Chat with { ExtraChatCount = 1 })]);
        var b = new ActivitySet([new LocationActivity(Chat with { ExtraChatCount = 1 })]);
        var c = new ActivitySet([new LocationActivity(Chat with { ExtraChatCount = 2 })]);

        // act & assert
        a.Should().Be(b);
        a.Should().NotBe(c);
        ActivitySet.Empty.Should().Be(new ActivitySet([]));
    }

    [Fact]
    public void ServiceTypesAreTheUnionTheSetNeeds()
    {
        // arrange
        var listening = new AudioActivity(ActivityKind.Listening, Chat, false);
        var recording = new AudioActivity(ActivityKind.Recording, Chat, false);
        var armed = new AudioActivity(ActivityKind.Armed, Chat, false, false);
        var location = new LocationActivity(Chat);
        var upload = new UploadActivity(1, 0, 100, ImmutableList<UploadActivityItem>.Empty);

        // act & assert
        new ActivitySet([listening]).GetServiceTypes().Should().Be(ActivityServiceTypes.Playback);
        new ActivitySet([recording]).GetServiceTypes()
            .Should().Be(ActivityServiceTypes.Playback | ActivityServiceTypes.Microphone);
        new ActivitySet([armed]).GetServiceTypes()
            .Should().Be(ActivityServiceTypes.Playback | ActivityServiceTypes.Microphone);
        new ActivitySet([listening, location, upload]).GetServiceTypes().Should().Be(
            ActivityServiceTypes.Playback | ActivityServiceTypes.Location | ActivityServiceTypes.DataSync);
        ActivitySet.Empty.GetServiceTypes().Should().Be(ActivityServiceTypes.None);
    }

    [Fact]
    public void UploadActivityEqualityIsByContent()
    {
        // arrange
        var items1 = ImmutableList.Create(new UploadActivityItem("s1", "f.bin", 10, 100));
        var items2 = ImmutableList.Create(new UploadActivityItem("s1", "f.bin", 10, 100));

        // act & assert
        new UploadActivity(1, 10, 100, items1).Should().Be(new UploadActivity(1, 10, 100, items2));
        new UploadActivity(1, 20, 100, items1).Should().NotBe(new UploadActivity(1, 10, 100, items2));
    }
}
