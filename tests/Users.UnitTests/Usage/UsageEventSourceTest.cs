using ActualChat.Contacts;
using ActualChat.Live;

namespace ActualChat.Users.UnitTests.Usage;

public class UsageEventSourceTest
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    private static readonly AuthorId TestAuthorId = AuthorId.New(TestChatId, 10);
    private static readonly UserId OwnerId = UserId.Parse("owner1");
    private static readonly UserId OtherId = UserId.Parse("other1");
    private static readonly Moment T0 = new(DateTime.UnixEpoch.AddYears(56));

    [Fact]
    public void TypedEntryShouldCountAsMessageOnCreateOnly()
    {
        // arrange
        var entry = NewTextEntry();

        // act
        var created = UsageEventSource.FromEntryChange(entry, null, ChangeKind.Create);
        var updated = UsageEventSource.FromEntryChange(entry, entry, ChangeKind.Update);

        // assert
        created.Should().NotBeNull();
        created!.Kind.Should().Be(UsageEventKind.Message);
        created.Value.Should().Be(1);
        created.SourceId.Should().Be(entry.Id.Value);
        updated.Should().BeNull("an edit is not a new message");
    }

    [Fact]
    public void VoiceEntryShouldCountOnceWhenEndsAtAppears()
    {
        // arrange
        var open = NewTextEntry() with { Audio = new ChatEntryAudio { StreamId = "s1" } };
        var closed = open with { EndsAt = T0 + TimeSpan.FromSeconds(12) };
        var edited = closed with { Content = "edited" };

        // act
        var onCreate = UsageEventSource.FromEntryChange(open, null, ChangeKind.Create);
        var onClose = UsageEventSource.FromEntryChange(closed, open, ChangeKind.Update);
        var onEdit = UsageEventSource.FromEntryChange(edited, closed, ChangeKind.Update);
        var onCreateClosed = UsageEventSource.FromEntryChange(closed, null, ChangeKind.Create);

        // assert
        onCreate.Should().BeNull("a streaming entry has no duration yet");
        onClose.Should().NotBeNull();
        onClose!.Kind.Should().Be(UsageEventKind.Speech);
        onClose.Value.Should().Be(12_000);
        onEdit.Should().BeNull("EndsAt was already set before this change");
        onCreateClosed!.Value.Should().Be(12_000, "an entry created complete counts on create");
    }

    [Fact]
    public void VoiceEntryDurationShouldBeCapped()
    {
        // arrange
        var open = NewTextEntry() with { Audio = new ChatEntryAudio { StreamId = "s1" } };
        var closed = open with { EndsAt = T0 + TimeSpan.FromDays(2) };

        // act
        var usageEvent = UsageEventSource.FromEntryChange(closed, open, ChangeKind.Update);

        // assert
        usageEvent!.Value.Should().Be((long)UsageEventSource.MaxSpeechDuration.TotalMilliseconds);
    }

    [Fact]
    public void SystemAndRemovedEntriesShouldNotCount()
    {
        // arrange
        var systemEntry = new MembersChangedEntry(ChatEntryId.New(TestChatId, 2)) {
            AuthorId = TestAuthorId,
            BeginsAt = T0,
        };
        var removed = NewTextEntry();

        // act
        var fromSystem = UsageEventSource.FromEntryChange(systemEntry, null, ChangeKind.Create);
        var fromRemove = UsageEventSource.FromEntryChange(removed, removed, ChangeKind.Remove);

        // assert
        fromSystem.Should().BeNull();
        fromRemove.Should().BeNull();
    }

    [Fact]
    public void LiveSessionShouldCountMembersWhoStayedLongEnough()
    {
        // arrange
        var early = new LiveSessionEndedMember(AuthorId.New(TestChatId, 1), T0);
        var late = new LiveSessionEndedMember(AuthorId.New(TestChatId, 2), T0 + TimeSpan.FromMinutes(4.5));
        var ended = new LiveSessionEndedEvent(
            TestChatId, T0, T0 + TimeSpan.FromMinutes(5), LiveSessionKind.Call, ApiArray.New(early, late));

        // act
        var fromEarly = UsageEventSource.FromLiveSessionEnd(ended, early);
        var fromLate = UsageEventSource.FromLiveSessionEnd(ended, late);

        // assert
        fromEarly.Should().NotBeNull();
        fromEarly!.Kind.Should().Be(UsageEventKind.LiveSession);
        fromEarly.Value.Should().Be(5 * 60_000);
        fromEarly.Attributes!.ParticipantCount.Should().Be(2);
        fromLate.Should().BeNull("30 seconds is below the minimum participation");
    }

    [Fact]
    public void LiveSessionSourceIdShouldBeStableAcrossRedelivery()
    {
        // arrange
        var member = new LiveSessionEndedMember(TestAuthorId, T0);
        var ended = new LiveSessionEndedEvent(
            TestChatId, T0, T0 + TimeSpan.FromMinutes(5), LiveSessionKind.Ambient, ApiArray.New(member));
        var redelivered = ended with { EndedAt = ended.EndedAt + TimeSpan.FromSeconds(1) };

        // act
        var first = UsageEventSource.FromLiveSessionEnd(ended, member);
        var second = UsageEventSource.FromLiveSessionEnd(redelivered, member);

        // assert
        second!.SourceId.Should().Be(first!.SourceId);
    }

    [Fact]
    public void ContactShouldCountOnTransitionsOnly()
    {
        // arrange
        var contactId = ContactId.NewUser(OwnerId, OtherId);
        var temporary = new Contact(contactId, 1) { UserId = OtherId, State = ContactState.Temporary };
        var regular = temporary with { Version = 2, State = ContactState.Regular };
        var pinned = regular with { Version = 3, IsPinned = true };
        var blocked = pinned with { Version = 4, State = ContactState.Blocked };

        // act
        var onTemporary = UsageEventSource.FromContactChange(temporary, null, ChangeKind.Create, T0);
        var onRegular = UsageEventSource.FromContactChange(regular, temporary, ChangeKind.Update, T0);
        var onPin = UsageEventSource.FromContactChange(pinned, regular, ChangeKind.Update, T0);
        var onBlock = UsageEventSource.FromContactChange(blocked, pinned, ChangeKind.Update, T0);
        var onRemove = UsageEventSource.FromContactChange(regular, regular, ChangeKind.Remove, T0);

        // assert
        onTemporary.Should().BeNull("a temporary contact is not a friend yet");
        onRegular!.Value.Should().Be(1);
        onRegular.SourceId.Should().Be($"{contactId}:2");
        onPin.Should().BeNull("pinning does not change friendship");
        onBlock!.Value.Should().Be(-1);
        onRemove!.Value.Should().Be(-1);
    }

    [Fact]
    public void ChatContactShouldNotBeAFriend()
    {
        // arrange
        var contact = new Contact(ContactId.NewAny(OwnerId, TestChatId), 1) { State = ContactState.Regular };

        // act
        var usageEvent = UsageEventSource.FromContactChange(contact, null, ChangeKind.Create, T0);

        // assert
        usageEvent.Should().BeNull();
    }

    [Fact]
    public void ActiveDayShouldBeKeyedByUtcDay()
    {
        // act
        var morning = UsageEventSource.ActiveDay(new Moment(new DateTime(2026, 9, 21, 1, 0, 0, DateTimeKind.Utc)));
        var evening = UsageEventSource.ActiveDay(new Moment(new DateTime(2026, 9, 21, 23, 59, 0, DateTimeKind.Utc)));

        // assert
        morning.SourceId.Should().Be("2026-09-21");
        evening.SourceId.Should().Be(morning.SourceId);
        evening.OccurredAt.Should().Be(morning.OccurredAt);
    }

    private static TextEntry NewTextEntry()
        => new(ChatEntryId.New(TestChatId, 1), 7) {
            AuthorId = TestAuthorId,
            BeginsAt = T0,
            Content = "Hello",
        };
}
