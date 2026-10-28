using System.Text;
using ActualChat.Chat;
using ActualChat.Serialization.Internal;

namespace ActualChat.Core.UnitTests.Identifiers;

public sealed class LanguageTest(ITestOutputHelper @out) : StringIdentifierTestBase<Language>(@out)
{
    private static readonly MessagePackSerializerOptions MessagePackOptions = new(AppMessagePackResolver.Instance);

    public override string[] ValidIdentifiers => [ "eN-Us", "UK", "rU" ];
    public override string[] InvalidIdentifiers => [ "X", "~", "max" ];

    [Fact]
    public void UnknownLanguageShouldDeserializeAsPlaceholder()
    {
        // act
        var fromMessagePack = MessagePackSerializer.Deserialize<Language>(
            MessagePackSerializer.Serialize("xx-XX", MessagePackOptions), MessagePackOptions);
        var fromSystemJson = new SystemJsonSerializer(JsonSerializerOptions.Default).Read<Language>("\"xx-XX\"");
        var fromNewtonsoftJson = new NewtonsoftJsonSerializer().Read<Language>("\"xx-XX\"");

        // assert
        fromMessagePack.Value.Should().Be("xx-XX");
        fromMessagePack.IsoCode.Should().Be("xx");
        fromMessagePack.Support.Should().Be(LanguageSupport.None, "a client can't offer a language it doesn't know");
        fromSystemJson.Should().BeSameAs(fromMessagePack, "Language equality is reference-based");
        fromNewtonsoftJson.Should().BeSameAs(fromMessagePack);
        Language.TryParse("xx-XX").Should().BeNull("only deserialization tolerates unknown languages");
    }

    [Fact]
    public void UnknownLanguageShouldNotFailLanguageTile()
    {
        // arrange
        var entryLanguage = new ChatEntryLanguage(ChatEntryId.Parse("the-actual-one:0:7"), 1) {
            Languages = [Languages.English, Languages.German],
        };
        var tile = new ChatLanguageTile(new Range<long>(0, 16), [entryLanguage]);
        var bytes = MessagePackSerializer.Serialize(tile, MessagePackOptions);
        bytes = ReplaceAscii(bytes, "de-DE", "xx-XX");

        // act
        var result = MessagePackSerializer.Deserialize<ChatLanguageTile>(bytes, MessagePackOptions);

        // assert
        var languages = result.Entries.Single().Languages;
        languages[0].Should().BeSameAs(Languages.English);
        languages[1].Value.Should().Be("xx-XX");
    }

    [Fact]
    public void UnknownLanguageShouldNotFailMemoryPackSettings()
    {
        // arrange
        var settings = new ChatUserSettings { Language = Languages.German };
        var bytes = MemoryPackSerializer.Serialize(settings);
        bytes = ReplaceAscii(bytes, "de-DE", "xx-XX");

        // act
        var result = MemoryPackSerializer.Deserialize<ChatUserSettings>(bytes)!;

        // assert
        result.Language!.Value.Should().Be("xx-XX");
    }

    [Theory]
    [InlineData("~")]
    [InlineData("kannada")]
    [InlineData("sr-Latn-RS")]
    public void AnyUnknownValueShouldDeserializeAsPlaceholder(string value)
    {
        // arrange
        var bytes = MessagePackSerializer.Serialize(value, MessagePackOptions);

        // act
        var result = MessagePackSerializer.Deserialize<Language>(bytes, MessagePackOptions);

        // assert
        result.Value.Should().Be(value, "only an empty value means no language");
        result.Support.Should().Be(LanguageSupport.None);
    }

    // Private methods

    private static byte[] ReplaceAscii(byte[] bytes, string oldValue, string newValue)
    {
        var oldBytes = Encoding.ASCII.GetBytes(oldValue);
        var index = bytes.AsSpan().IndexOf(oldBytes);
        index.Should().BeGreaterThanOrEqualTo(0);
        var result = bytes.ToArray();
        Encoding.ASCII.GetBytes(newValue).CopyTo(result, index);
        return result;
    }
}
