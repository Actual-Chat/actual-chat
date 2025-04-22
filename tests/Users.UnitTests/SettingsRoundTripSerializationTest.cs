using ActualChat.Kvas;
using ActualLab.IO;

namespace ActualChat.Users.UnitTests;

public class SettingsRoundTripSerializationTest
{
    [Fact]
    public async Task ShouldDeserializeCorrectly()
    {
        foreach (var (name, expected) in GetCases()) {
            // arrange
            var bytes = await File.ReadAllBytesAsync($"Data/{GetFileName(expected, name)}");

            // act
            var data = KvasSerializer.Default.Read(bytes, expected.GetType());

            // assert
            data.Should().BeEquivalentTo(expected);
        }
    }

    [Fact(Skip = "Does not work")]
    public async Task ShouldSerializeBackwardCompatible()
    {
        foreach (var (name, expected) in GetCases()) {
            // arrange
            var bytes = await File.ReadAllBytesAsync($"Data/{GetFileName(expected, name)}");

            // act
            using var newVersionBuffer = KvasSerializer.Default.Write(expected, expected.GetType());
            var newVersionBytes = newVersionBuffer.WrittenMemory.ToArray();

            // assert
            newVersionBytes.Should().BeEquivalentTo(bytes, "test case '{0}'", name);
        }
    }

    [Fact(Skip = "Only for manual runs to generate new test data")]
    // [Fact] // NOTE: only for manual runs to generate test data.
    // It must be run on a previous version any earlier version to keep backward compatibility.
    public async Task GenerateTestCases()
    {
        if (TestRunnerInfo.IsBuildAgent())
            return;

        FilePath baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var outDir = baseDir | "../../../../../tests/Users.UnitTests/Data";
        outDir = outDir.ToAbsolute();
        if (Directory.Exists(outDir))
            Directory.Delete(outDir, true);
        Directory.CreateDirectory(outDir);
        foreach (var (name, data) in GetCases()) {
            using var buffer = KvasSerializer.Default.Write(data, data.GetType());
            await File.WriteAllBytesAsync(outDir | GetFileName(data, name), buffer.WrittenMemory.ToArray());
        }
    }

    private static string GetFileName(object data, string name)
        => $"{data.GetType().Name}.{name}.bin";

    private IEnumerable<(string Name, object Value)> GetCases()
    {
        foreach (var x in GetUserChatSettingsCases())
            yield return x;

        foreach (var x in GetUserLanguageSettingsCases())
            yield return x;
    }

    private IEnumerable<(string Name, UserChatSettings Data)> GetUserChatSettingsCases()
    {
        yield return ("DefaultInstance", UserChatSettings.Default);
        yield return ("Empty", new ());
        yield return ("OnlyLanguageEnglish", new () {
            Language = Languages.English,
        });
        yield return ("OnlyLanguageRussian", new () {
            Language = Languages.Russian,
        });
        yield return ("ListeningMode5Minutes", new () {
            ListeningMode = ListeningMode.For5Minutes,
        });
        yield return ("NotificationModeImportantOnly", new () {
            NotificationMode = ChatNotificationMode.ImportantOnly,
        });
        yield return ("VoiceModeJustVoice", new () {
            VoiceMode = VoiceMode.JustVoice,
        });
        yield return ("AllProperties", new () {
            Language = Languages.EnglishUK,
            ListeningMode = ListeningMode.For1Hour,
            NotificationMode = ChatNotificationMode.Muted,
            VoiceMode = VoiceMode.JustText,
        });
    }

    private IEnumerable<(string Name, UserLanguageSettings Data)> GetUserLanguageSettingsCases()
    {
        yield return ("Empty", new ());
        yield return ("OnlyOrigin", new () {
            Origin = "https://actual.chat",
        });
        yield return ("OnlyPrimaryEnglish", new () {
            Primary = Languages.EnglishUK,
        });
        yield return ("OnlySecondaryRussian", new () {
            Secondary = Languages.Russian,
        });
        yield return ("OnlyTertiaryGerman", new () {
            Tertiary = Languages.German,
        });
        yield return ("AllProperties", new () {
            Primary = Languages.Russian,
            Secondary = Languages.English,
            Tertiary = Languages.Italian,
            Origin = "https://actual.chat",
        });
    }
}
