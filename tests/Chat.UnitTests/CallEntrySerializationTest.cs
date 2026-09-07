namespace ActualChat.Chat.UnitTests;

public class CallEntrySerializationTest
{
    [Fact]
    public void CallEntryShouldRoundTripAsChatEntry()
    {
        // arrange
        var chatId = ChatId.Parse("052w3sgrad");
        var caller = AuthorId.New(chatId, 1);
        var invitee = AuthorId.New(chatId, 2);
        ChatEntry entry = new CallEntry(ChatEntryId.New(chatId, 7), 3) {
            CallerId = caller,
            CallerName = "John",
            Outcome = CallOutcome.NoAnswer,
            InviteeIds = new[] { invitee }.ToApiArray(),
            HasVideo = true,
        };

        // act
        var read = entry.PassThroughModernSerializers();

        // assert
        var call = read.Should().BeOfType<CallEntry>().Subject;
        call.Id.Should().Be(entry.Id);
        call.Version.Should().Be(3);
        call.CallerId.Should().Be(caller);
        call.CallerName.Should().Be("John");
        call.Outcome.Should().Be(CallOutcome.NoAnswer);
        call.InviteeIds.Should().Equal(invitee);
        call.HasVideo.Should().BeTrue();
    }

    [Fact]
    public void CallEntryTagShouldBeInTheSystemRange()
    {
        // The 100..199 range is what ChatEntry.IsSystemUnionTag reads: a call entry outside it
        // would be rebuilt as a message, not a system entry, by a peer that doesn't know the tag.

        // act
        var tag = ChatEntry.GetUnionTag(new CallEntry());

        // assert
        tag.Should().NotBeNull();
        ChatEntry.IsSystemUnionTag(tag!.Value).Should().BeTrue();
    }

    [Fact]
    public void CallEntryShouldDeclareItsRelease()
    {
        // act
        var tag = ChatEntry.GetUnionTag(new CallEntry())!.Value;

        // assert
        ChatEntry.GetUnionTagSince(tag).Should().Be(new Version(2, 19));
    }

    [Fact]
    public void CallEntryShouldRoundTripThroughTheLegacyEnvelope()
    {
        // The Content column stores system entries in the frozen v2.7 wrapper shape; a new kind
        // needs its own named property there, because the row carries no other discriminator.

        // arrange
        var chatId = ChatId.Parse("052w3sgrad");
        var caller = AuthorId.New(chatId, 1);
        var entry = new CallEntry(ChatEntryId.New(chatId, 7)) {
            CallerId = caller,
            CallerName = "John",
            Outcome = CallOutcome.Ended,
            InviteeIds = new[] { AuthorId.New(chatId, 2) }.ToApiArray(),
            HasVideo = true,
        };

        // act
        var json = Serializers.SystemJson.Write(LegacySystemEntry.From(entry)!);
        var back = Serializers.SystemJson.Read<LegacySystemEntry>(json);

        // assert
        var option = back.Option.Should().BeOfType<LegacyCallOption>().Subject;
        option.CallerId.Should().Be(caller);
        option.CallerName.Should().Be("John");
        option.Outcome.Should().Be(CallOutcome.Ended);
        option.HasVideo.Should().BeTrue();
    }
}
