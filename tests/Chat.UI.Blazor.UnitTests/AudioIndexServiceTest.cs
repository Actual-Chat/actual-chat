using ActualChat.Chat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class AudioIndexServiceTest
{
    [Fact]
    public void AudioIndexTest()
    {
        var audioIndex = new AudioIndexService();
        // @formatter:off
        audioIndex.AddAudioEntries(new[] {
            new ChatEntry {
                Id = 2,
                ContentType = ChatContentType.Audio,
                BeginsAt = new Moment(new DateTime(2021, 10, 15, 16, 3, 3, 300)),
                EndsAt = new Moment(new DateTime(2021, 10, 15, 16, 5, 3, 300)),
            },
            new ChatEntry {
                Id = 4,
                ContentType = ChatContentType.Audio,
                BeginsAt = new Moment(new DateTime(2021, 10, 15, 16, 8, 3, 100)),
                EndsAt = new Moment(new DateTime(2021, 10, 15, 16, 12, 3, 100)),
            },
            new ChatEntry {
                Id = 7,
                ContentType = ChatContentType.Audio,
                BeginsAt = new Moment(new DateTime(2021, 10, 15, 16, 11, 3, 300)),
                EndsAt = new Moment(new DateTime(2021, 10, 15, 16, 15, 3, 100)),
            },
            new ChatEntry {
                Id = 12,
                ContentType = ChatContentType.Audio,
                BeginsAt = new Moment(new DateTime(2021, 10, 15, 16, 15, 3, 650)),
                EndsAt = new Moment(new DateTime(2021, 10, 15, 16, 18, 3, 100)),
            },
        });

        var firstEntry = audioIndex.FindAudioEntry(
            new ChatEntry {
                Id = 1,
                ContentType = ChatContentType.Audio,
                BeginsAt = new Moment(new DateTime(2021, 10, 15, 16, 3, 3, 300)),
                EndsAt = new Moment(new DateTime(2021, 10, 15, 16, 5, 3, 300)),
            },
            TimeSpan.FromSeconds(0));

        firstEntry.Id.Should().Be(2);

        var secondEntry = audioIndex.FindAudioEntry(
            new ChatEntry {
                Id = 3,
                ContentType = ChatContentType.Audio,
                BeginsAt = new Moment(new DateTime(2021, 10, 15, 16, 11, 25, 300)),
                EndsAt = new Moment(new DateTime(2021, 10, 15, 16, 17, 3, 300)),
            },
            TimeSpan.FromSeconds(5));

        secondEntry.Id.Should().Be(4);

        var thirdEntry = audioIndex.FindAudioEntry(
            new ChatEntry {
                Id = 6,
                ContentType = ChatContentType.Audio,
                BeginsAt = new Moment(new DateTime(2021, 10, 15, 16, 8, 25, 300)),
                EndsAt = new Moment(new DateTime(2021, 10, 15, 16, 17, 3, 300)),
            },
            TimeSpan.FromMinutes(5));

        thirdEntry.Id.Should().Be(7);

        // @formatter:on
    }
}
