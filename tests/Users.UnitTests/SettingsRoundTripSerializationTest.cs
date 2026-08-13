using ActualChat.Kvas;
using ActualLab.IO;

namespace ActualChat.Users.UnitTests;

public class SettingsRoundTripSerializationTest
{
    [Theory]
    [MemberData(nameof(GetCases))]
    public async Task ShouldDeserializePreviousVersion(string name, object expected)
    {
        // arrange
        var bytes = await File.ReadAllBytesAsync($"Data/{GetFileName(expected, name)}");

        // act
        var data = KvasSerializer.Default.Read(bytes, expected.GetType(), out _);

        // assert
        data.Should().BeEquivalentTo(expected);
    }

    [Theory]
    [MemberData(nameof(GetCases))]
    public void ShouldSerializeDeserializeForLatestVersion(string name, object expected)
    {
        // act
        using var buffer = KvasSerializer.Default.Write(expected, expected.GetType());
        var bytes = buffer.WrittenMemory.ToArray();
        var deserialized = KvasSerializer.Default.Read(bytes, expected.GetType(), out _);

        // assert
        deserialized.Should().BeEquivalentTo(expected, "test case '{0}'", name);
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
        foreach (var row in GetCases()) {
            var name = row[0] as string;
            var data = row[1]!;
            using var buffer = KvasSerializer.Default.Write(data, data.GetType());
            await File.WriteAllBytesAsync(outDir | GetFileName(data, name), buffer.WrittenMemory.ToArray());
        }
    }

    public static TheoryData<string, object> GetCases()
    {
        var theoryData = new TheoryData<string, object>();
        foreach (var (name, data) in GetChatUserSettingsCases())
            theoryData.Add(name, data);
        foreach (var (name, data) in GetUserLanguageSettingsCases())
            theoryData.Add(name, data);
        return theoryData;
    }

    // Private methods

    private static string GetFileName(object data, string name)
        => $"{data.GetType().Name}.{name}.bin";

    private static IEnumerable<(string Name, ChatUserSettings Data)> GetChatUserSettingsCases()
    {
        yield return ("DefaultInstance", ChatUserSettings.Default);
        yield return ("Empty", new ());
        yield return ("OnlyLanguageEnglish", new () {
            Language = Languages.English,
        });
        yield return ("OnlyLanguageRussian", new () {
            Language = Languages.Russian,
        });
        // The stored blob has the old ListeningMode = For5Minutes (5); the write-only
        // [Obsolete] stub keeps the raw value on read.
#pragma warning disable CS0618
        yield return ("ListeningMode5Minutes", new () {
            ListeningMode = 5,
        });
#pragma warning restore CS0618
        yield return ("NotificationModeImportantOnly", new () {
            NotificationMode = ChatNotificationMode.ImportantOnly,
        });
        yield return ("VoiceModeJustVoice", new () {
            VoiceMode = VoiceMode.JustVoice,
        });
        // The stored blob also has the old ListeningMode = For1Hour (60).
#pragma warning disable CS0618
        yield return ("AllProperties", new () {
            Language = Languages.EnglishUK,
            ListeningMode = 60,
            NotificationMode = ChatNotificationMode.Muted,
            VoiceMode = VoiceMode.JustText,
        });
#pragma warning restore CS0618
    }

    private static IEnumerable<(string Name, UserLanguageSettings Data)> GetUserLanguageSettingsCases()
    {
        yield return ("Empty", new ());
        yield return ("OnlyOrigin", new () {
            Origin = $"https://actual.chat",
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
            Origin = $"https://actual.chat",
        });
    }
}
