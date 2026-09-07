# Call Outcome Entries Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Record what happened to a call — no answer, declined, canceled, or ended — as a `CallEntry` in the peer chat, so a call stops vanishing without trace.

**Architecture:** One `CallEntry : SystemEntry` carrying a `CallOutcome` enum, written from the single funnel where a call session closes. The three failed outcomes render as a small card of their own; `Ended` is skipped in the message list and drawn by the existing conversation item in call mode, which requires a connected call to materialize its conversation.

**Tech Stack:** .NET 11 (`net11.0`, `LangVersion=preview`, SDK pinned in `global.json`), MessagePack unions, EF Core + PostgreSQL, Blazor, xUnit + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-09-02-call-outcome-entries-design.md`

## Global Constraints

- **Read `docs/CODING_STYLE.md` before writing any C# or Razor.** This project deviates from standard .NET conventions: no `Async` suffix on async methods, no XML docs on members, mixed brace style. A style hook checks every `.cs`/`.ts`/`.razor`/`.css` edit.
- **Comments:** read `docs/CODING_STYLE.md → "Regular comments, docstrings, XML documentation comments"` before adding any `//`. Comments explain *why*, never restate the code.
- **Union tags are load-bearing.** `CallEntry` takes tag **101** on `ChatEntry` (the 100..199 `SystemEntry` range that `ChatEntry.IsSystemUnionTag` reads) and tag **3** on `SystemEntry`. It must declare `[101] = new (2, 19)` in `ChatEntry.UnionTagSinceVersions`, or `IChats.GetLegacyTile` will ship it to pre-2.19 peers that cannot read it.
- **Peer chats only.** Every emission is gated on `chatId.Kind == ChatKind.Peer`.
- **Base record owns keys 0..19.** `CallEntry`'s own `[Key]` indices start at 20.
- **`LangVersion` is `preview`,** and the codebase uses the `field` keyword rather than hand-written backing fields — see `DbChatEntry.BeginsAt`. Match the file you are editing.
- **Localization:** every new key goes into all hand-written `Strings.*.json`, then `scripts/derive-bcms.cmd` and `scripts/derive-max.cmd` regenerate the derived catalogs. See `docs/i18n.md`.
- **Branch:** `feat/call-outcome-entries`, based on `feat/forward-compatible-unions`. Do not rebase it onto `origin/dev` — the tolerance commits underneath are required.

---

## File Structure

**Created:**

| File | Responsibility |
| --- | --- |
| `src/dotnet/Api/Chat/CallEntry.cs` | `CallOutcome` enum and the `CallEntry` record |
| `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Call/CallCardFormat.cs` | The one outcome × is-caller → icon/title/hint mapping, shared by both render paths |
| `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Call/CallMessageView.razor` | The card for the three failed outcomes |
| `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Call/call-message.css` | Its styles |
| `src/dotnet/Chat.Service.Migration/Migrations/*_Add_Conversation_IsCall.cs` | Generated migration |
| `tests/Chat.IntegrationTests/CallEntryTest.cs` | Write-path tests, end to end through the real backend |
| `tests/Chat.UI.Blazor.UnitTests/CallCardFormatTest.cs` | The wording table |

**Modified:**

| File | Change |
| --- | --- |
| `src/dotnet/Api/Chat/ChatEntry.cs` | `[Union(101, …)]`, `ChatEntryKind.Call`, `NewEmpty`, `ChatEntryDiff` fields and ctor |
| `src/dotnet/Api/Chat/SystemEntry.cs` | `[Union(3, …)]` |
| `src/dotnet/Api/Chat/ChatEntry.Unsupported.cs` | `UnionTagSinceVersions[101]` |
| `src/dotnet/Api/Chat/LegacySystemEntry.cs` | `LegacyCallOption`, the `Call` property, delete the dead `From` |
| `src/dotnet/Chat.Service/Db/DbChatEntry.cs` | `ToModel` and `ToLegacySystemEntry` arms |
| `src/dotnet/Api/Chat/Conversation.cs` | `IsCall` on the record and on `ConversationDiff` |
| `src/dotnet/Chat.Service/Db/DbConversation.cs` | `IsCall` column, read and write |
| `src/dotnet/Api/Live/LiveSessionState.cs` | `Outcome`, `HasVideo` |
| `src/dotnet/Api/Live/LiveSessionState.cs` | `ToMaterializedConversation` sets `IsCall` |
| `src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs` | Outcome recording, emission, materializing a connected call |
| `src/dotnet/Api/Constants.cs` | `Walle.GetWalleAuthorId` |
| `src/dotnet/Chat.Service/Bots.cs` | `GetWalleId` delegates to it |
| `src/dotnet/Api/Chat/Markup/SystemEntryMarkupBuilder.cs` | `BuildCall` |
| `src/dotnet/UI.Blazor.App/Services/LocalizedSystemEntryMarkupBuilder.cs` | `BuildCall` override |
| `src/dotnet/Localization/Resources/Strings.*.json` | 11 keys |
| `src/dotnet/UI.Blazor.App/Components/ChatView/Items/ChatEntryMessageView.razor` | Route `CallEntry` to `CallMessageView` |
| `src/dotnet/UI.Blazor.App/Services/ChatUI.Tiles.cs` | Skip `Ended` |
| `src/dotnet/Api/Chat/ChatNews.cs` | `WithoutEntryUnknownTo` |
| `src/dotnet/Api.Contracts/Chat/IChats.cs` | `GetLegacyNews` and its version bands |
| `src/dotnet/Chat.Service/Chats.cs` | `GetLegacyNews` |
| `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/ConversationMessageHeader.razor` | Call mode |
| `tests/Chat.UI.Blazor.UnitTests/SystemEntryLocalizationTest.cs` | `CallEntry` samples |

---

### Task 1: The `CallEntry` contract

**Files:**
- Create: `src/dotnet/Api/Chat/CallEntry.cs`
- Modify: `src/dotnet/Api/Chat/ChatEntry.cs`, `src/dotnet/Api/Chat/SystemEntry.cs`, `src/dotnet/Api/Chat/ChatEntry.Unsupported.cs`
- Test: `tests/Chat.UnitTests/CallEntrySerializationTest.cs`

**Interfaces:**
- Produces: `ActualChat.Chat.CallOutcome` (`None`/`NoAnswer`/`Declined`/`Canceled`/`Ended`); `ActualChat.Chat.CallEntry` with `AuthorId CallerId`, `string CallerName`, `CallOutcome Outcome`, `ApiArray<AuthorId> InviteeIds`, `bool HasVideo`; `ChatEntryKind.Call`.

- [ ] **Step 1: Write the failing test**

`tests/Chat.UnitTests/CallEntrySerializationTest.cs`:

```csharp
using ActualLab.Serialization;

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
        var data = Serializers.MessagePack.Write(entry);
        var read = Serializers.MessagePack.Read<ChatEntry>(data);

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
        var tag = ChatEntry.GetUnionTag(new CallEntry());
        tag.Should().NotBeNull();
        ChatEntry.IsSystemUnionTag(tag!.Value).Should().BeTrue();
    }

    [Fact]
    public void CallEntryShouldDeclareItsRelease()
    {
        var tag = ChatEntry.GetUnionTag(new CallEntry())!.Value;
        ChatEntry.GetUnionTagSince(tag).Should().Be(new Version(2, 19));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Chat.UnitTests/Chat.UnitTests.csproj --filter FullyQualifiedName~CallEntrySerializationTest`
Expected: FAIL — `CallEntry` does not exist (compile error).

- [ ] **Step 3: Create the entry type**

`src/dotnet/Api/Chat/CallEntry.cs`:

```csharp
namespace ActualChat.Chat;

public enum CallOutcome
{
    None = 0,
    NoAnswer = 1,
    Declined = 2,
    Canceled = 3,
    Ended = 4,
}

/// <summary>
/// System entry recording how a call went: it never connected (<see cref="CallOutcome.NoAnswer"/>,
/// <see cref="CallOutcome.Declined"/>, <see cref="CallOutcome.Canceled"/>) or it connected and
/// finished (<see cref="CallOutcome.Ended"/>).
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record CallEntry : SystemEntry
{
    [DataMember, Key(20)] public AuthorId CallerId { get; init; } = null!;
    [DataMember, Key(21)] public string CallerName { get; init; } = "";
    [DataMember, Key(22)] public CallOutcome Outcome { get; init; }
    [DataMember, Key(23)] public ApiArray<AuthorId> InviteeIds { get; init; }
    [DataMember, Key(24)] public bool HasVideo { get; init; }

    public CallEntry() : base((ChatEntryId)null!) { }

    [SerializationConstructor]
    public CallEntry(ChatEntryId id, long version = 0) : base(id, version) { }
}
```

- [ ] **Step 4: Register the union tags and the kind**

In `src/dotnet/Api/Chat/ChatEntry.cs`, after the existing `[Union(100, typeof(UnsupportedSystemEntry))]` line:

```csharp
[Union(101, typeof(CallEntry))]
```

In `src/dotnet/Api/Chat/SystemEntry.cs`, after `[Union(2, typeof(UnsupportedSystemEntry))]`:

```csharp
[Union(3, typeof(CallEntry))]
```

In `ChatEntry.cs`, extend `ChatEntryKind`:

```csharp
public enum ChatEntryKind {
    Text = 0,
    MembersChanged = 1,
    NotifyMembers = 2,
    Call = 3,
}
```

and the two switches that map it — `NewEmpty`:

```csharp
ChatEntryKind.Call => new CallEntry(id),
```

and the `ChatEntryDiff(ChatEntry entry)` constructor:

```csharp
CallEntry => ChatEntryKind.Call,
```

- [ ] **Step 5: Declare the release**

In `src/dotnet/Api/Chat/ChatEntry.Unsupported.cs`, add to `UnionTagSinceVersions`:

```csharp
[101] = new (2, 19), // CallEntry
```

- [ ] **Step 6: Add the diff fields**

In `ChatEntry.cs`, in the `// System-entry payload` block of `ChatEntryDiff`:

```csharp
[DataMember] public AuthorId? CallerId { get; init; }
[DataMember] public string? CallerName { get; init; }
[DataMember] public CallOutcome? Outcome { get; init; }
[DataMember] public ApiArray<AuthorId>? InviteeIds { get; init; }
[DataMember] public bool? HasVideo { get; init; }
```

Names must match `CallEntry`'s exactly — `DiffEngine.DynamicPatch` matches by property name.

Also copy them in the `ChatEntryDiff(ChatEntry entry)` constructor:

```csharp
if (entry is CallEntry call) {
    CallerId = call.CallerId;
    CallerName = call.CallerName;
    Outcome = call.Outcome;
    InviteeIds = call.InviteeIds;
    HasVideo = call.HasVideo;
}
```

- [ ] **Step 7: Run the tests**

Run: `dotnet test tests/Chat.UnitTests/Chat.UnitTests.csproj --filter FullyQualifiedName~CallEntrySerializationTest`
Expected: PASS, 3 tests.

- [ ] **Step 8: Run the union guard tests**

Run: `dotnet test tests/Chat.UnitTests/Chat.UnitTests.csproj --filter "FullyQualifiedName~UnionToleranceCoverageTest|FullyQualifiedName~ForwardCompatibleUnionTest"`
Expected: PASS. These are the tolerance branch's guards; they fail if the tag is outside the system range or its release is undeclared.

- [ ] **Step 9: Commit**

```bash
git add src/dotnet/Api/Chat/CallEntry.cs src/dotnet/Api/Chat/ChatEntry.cs src/dotnet/Api/Chat/SystemEntry.cs src/dotnet/Api/Chat/ChatEntry.Unsupported.cs tests/Chat.UnitTests/CallEntrySerializationTest.cs
git commit -m "feat(chat): add the CallEntry system entry"
```

---

### Task 2: Persisting a `CallEntry`

**Files:**
- Modify: `src/dotnet/Api/Chat/LegacySystemEntry.cs`, `src/dotnet/Chat.Service/Db/DbChatEntry.cs`
- Test: `tests/Chat.UnitTests/CallEntrySerializationTest.cs` (extend)

**Interfaces:**
- Consumes: `CallEntry`, `CallOutcome` from Task 1.
- Produces: `LegacyCallOption(AuthorId CallerId, string CallerName, CallOutcome Outcome, AuthorId[] InviteeIds, bool HasVideo)`; `LegacySystemEntry.Call`.

- [ ] **Step 1: Write the failing test**

Append to `tests/Chat.UnitTests/CallEntrySerializationTest.cs`:

```csharp
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
```

This test exercises `LegacySystemEntry.From`, which has no callers today while `DbChatEntry.ToLegacySystemEntry` does the same job privately. Step 4 collapses the two into one by making the private one delegate, so the conversion this test covers is the conversion the database actually uses.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Chat.UnitTests/Chat.UnitTests.csproj --filter FullyQualifiedName~CallEntryShouldRoundTripThroughTheLegacyEnvelope`
Expected: FAIL — `LegacyCallOption` does not exist.

- [ ] **Step 3: Add the option and the envelope property**

In `src/dotnet/Api/Chat/LegacySystemEntry.cs`, add the property beside its siblings:

```csharp
    [DataMember]
    public LegacyCallOption? Call {
        get => Option as LegacyCallOption;
        init => Option ??= value;
    }
```

extend `From`:

```csharp
        CallEntry c => new LegacySystemEntry {
            Option = new LegacyCallOption(
                c.CallerId, c.CallerName, c.Outcome, c.InviteeIds.ToArray(), c.HasVideo),
        },
```

and add the option type at the end of the file:

```csharp
[DataContract]
public sealed partial record LegacyCallOption : LegacySystemEntryOption
{
    [DataMember] public AuthorId CallerId { get; init; } = null!;
    [DataMember] public string CallerName { get; init; } = "";
    [DataMember] public CallOutcome Outcome { get; init; }
    [DataMember] public AuthorId[] InviteeIds { get; init; } = [];
    [DataMember] public bool HasVideo { get; init; }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public LegacyCallOption(
        AuthorId callerId, string callerName, CallOutcome outcome, AuthorId[] inviteeIds, bool hasVideo)
    {
        CallerId = callerId;
        CallerName = callerName;
        Outcome = outcome;
        InviteeIds = inviteeIds;
        HasVideo = hasVideo;
    }
}
```

- [ ] **Step 4: Wire the DB conversions**

In `src/dotnet/Chat.Service/Db/DbChatEntry.cs`, add an arm to the `ToModel` switch **before** the `_ =>` fallback:

```csharp
                LegacyCallOption c => new CallEntry(id, Version) {
                    CallerId = c.CallerId,
                    CallerName = c.CallerName,
                    Outcome = c.Outcome,
                    InviteeIds = c.InviteeIds.ToApiArray(),
                    HasVideo = c.HasVideo,
                },
```

and replace the body of `ToLegacySystemEntry` with a call to the now-shared conversion:

```csharp
    private static LegacySystemEntry ToLegacySystemEntry(SystemEntry sys)
        => LegacySystemEntry.From(sys)
            ?? throw StandardError.Internal($"Unknown system entry: {sys.GetType().Name}");
```

- [ ] **Step 5: Run the test**

Run: `dotnet test tests/Chat.UnitTests/Chat.UnitTests.csproj --filter FullyQualifiedName~CallEntrySerializationTest`
Expected: PASS, 4 tests.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/Api/Chat/LegacySystemEntry.cs src/dotnet/Chat.Service/Db/DbChatEntry.cs tests/Chat.UnitTests/CallEntrySerializationTest.cs
git commit -m "feat(chat): persist CallEntry through the legacy system-entry envelope"
```

---

### Task 3: `Conversation.IsCall`

**Files:**
- Modify: `src/dotnet/Api/Chat/Conversation.cs`, `src/dotnet/Chat.Service/Db/DbConversation.cs`
- Create: `src/dotnet/Chat.Service.Migration/Migrations/*_Add_Conversation_IsCall.cs` (generated)
- Test: `tests/Chat.IntegrationTests/CallEntryTest.cs` (created in Task 5 — this task's verification is the build plus the migration diff)

**Interfaces:**
- Produces: `Conversation.IsCall` (`bool`), `ConversationDiff.IsCall` (`bool?`), `DbConversation.IsCall`.

- [ ] **Step 1: Add the field to the record and its diff**

In `src/dotnet/Api/Chat/Conversation.cs`, after `IsExpandedByDefault`:

```csharp
    [DataMember, Key(14)] public bool IsCall { get; init; }
```

In `ConversationDiff`, after its `IsExpandedByDefault`:

```csharp
    [DataMember] public bool? IsCall { get; init; }
```

and in the `ConversationDiff(Conversation conversation)` constructor:

```csharp
        IsCall = conversation.IsCall;
```

- [ ] **Step 2: Add the column**

In `src/dotnet/Chat.Service/Db/DbConversation.cs`, after `IsExpandedByDefault`:

```csharp
    public bool IsCall { get; set; }
```

Then set it in both directions — in `ToModel()`'s object initializer:

```csharp
            IsCall = IsCall,
```

and in `UpdateFrom(Conversation model)`:

```csharp
        IsCall = model.IsCall;
```

- [ ] **Step 3: Generate the migration**

```bash
dotnet ef migrations add Add_Conversation_IsCall --project src/dotnet/Chat.Service.Migration
```

Expected: a new pair of files under `src/dotnet/Chat.Service.Migration/Migrations/`, naming matching the existing convention (`20260819081048_Tighten_ChatEntry_ContentStreamId_Index.cs`).

- [ ] **Step 4: Check the migration is additive**

Read the generated `Up` method. It must contain exactly one `AddColumn<bool>` on `Conversations` with `defaultValue: false` and nothing else. If it contains anything else, the model has drifted — stop and report rather than editing the migration by hand.

- [ ] **Step 5: Build**

Run: `dotnet build src/dotnet/Chat.Service/Chat.Service.csproj`
Expected: succeeds.

- [ ] **Step 6: Commit**

```bash
git add src/dotnet/Api/Chat/Conversation.cs src/dotnet/Chat.Service/Db/DbConversation.cs src/dotnet/Chat.Service.Migration/Migrations
git commit -m "feat(chat): mark a conversation as a call"
```

---

### Task 4: Recording the outcome on the session

**Files:**
- Modify: `src/dotnet/Api/Live/LiveSessionState.cs`, `src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs`
- Test: `tests/Chat.IntegrationTests/LiveSessionsTest.cs` (extend)

**Interfaces:**
- Consumes: `CallOutcome` from Task 1.
- Produces: `LiveSessionState.Outcome` (`CallOutcome`), `LiveSessionState.HasVideo` (`bool`).

- [ ] **Step 1: Write the failing tests**

Append to `tests/Chat.IntegrationTests/LiveSessionsTest.cs`. These follow the file's existing shape — see `StartCallShouldSetDialingStatus` for the arrange block:

```csharp
    [Fact]
    public async Task DeclineShouldRecordDeclinedOutcome()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bobAuthor, aliceAuthor) = await NewTwoPartyCall(tester);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act
        await backend.StartCall(chatId, bobAuthor.Id, new[] { aliceAuthor.Id }.ToApiArray(), false, default);
        await backend.DeclineCall(chatId, aliceAuthor.Id, default);

        // assert — read before the close drops the state
        var state = await backend.GetState(chatId, default);
        state?.Outcome.Should().Be(CallOutcome.Declined);
    }

    [Fact]
    public async Task CancelAfterDeclineShouldKeepDeclined()
    {
        // First writer wins: a caller hanging up a moment after the invitee declined must not
        // rewrite the story as "canceled".
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bobAuthor, aliceAuthor) = await NewTwoPartyCall(tester);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act
        await backend.StartCall(chatId, bobAuthor.Id, new[] { aliceAuthor.Id }.ToApiArray(), false, default);
        await backend.DeclineCall(chatId, aliceAuthor.Id, default);
        await backend.CancelCall(chatId, bobAuthor.Id, default);

        // assert
        var state = await backend.GetState(chatId, default);
        state?.Outcome.Should().NotBe(CallOutcome.Canceled);
    }

    [Fact]
    public async Task StartCallShouldRememberHasVideo()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bobAuthor, aliceAuthor) = await NewTwoPartyCall(tester);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act
        await backend.StartCall(chatId, bobAuthor.Id, new[] { aliceAuthor.Id }.ToApiArray(), true, default);

        // assert
        var state = await backend.GetState(chatId, default);
        state!.HasVideo.Should().BeTrue();
    }
```

Add the helper at the end of the class, modelled on the arrange block of `StartCallShouldRingInvitee`:

```csharp
    private static async Task<(ChatId ChatId, AuthorFull Bob, AuthorFull Alice)> NewTwoPartyCall(
        IWebTester tester)
    {
        var bob = await tester.SignInAsUniqueBob();
        var alice = await tester.SignInAsUniqueAlice();
        await tester.SignIn(bob.User);
        var chatId = PeerChatId.New(bob.Id, alice.Id).ToChatId();
        var authors = tester.AppServices.GetRequiredService<IAuthorsBackend>();
        var bobAuthor = await authors.EnsureJoined(chatId, bob.Id, default);
        var aliceAuthor = await authors.EnsureJoined(chatId, alice.Id, default);
        return (chatId, bobAuthor, aliceAuthor);
    }
```

If `IAuthorsBackend.EnsureJoined` has a different signature in this tree, mirror whatever `ChatBlockTest.cs` does to obtain peer-chat authors — that file already builds a `PeerChatId.New(alice.Id, bob.Id)` and is the reference for this arrange block.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter "FullyQualifiedName~DeclineShouldRecordDeclinedOutcome|FullyQualifiedName~CancelAfterDeclineShouldKeepDeclined|FullyQualifiedName~StartCallShouldRememberHasVideo"`
Expected: FAIL — `LiveSessionState.Outcome` / `HasVideo` do not exist.

Infrastructure (PostgreSQL, Redis, NATS) must already be running; see `CLAUDE.md`.

- [ ] **Step 3: Add the two fields**

In `src/dotnet/Api/Live/LiveSessionState.cs`, after `IsExpandedByDefault` (which holds 21):

```csharp
    [DataMember(Order = 22), Key(22)]
    public CallOutcome Outcome { get; init; }
    [DataMember(Order = 23), Key(23)]
    public bool HasVideo { get; init; }
```

Redis-only state, so no migration; an older value reads back as `None`/`false`.

- [ ] **Step 4: Record `HasVideo` in `StartCall`**

In `LiveSessionsBackend.StartCall`, in the `state = (state ?? new LiveSessionState { … }) with { … }` initializer, add:

```csharp
                HasVideo = hasVideo,
```

`hasVideo` is already the method's parameter; today it only reaches `NotificationsBackend_NotifyCall`.

- [ ] **Step 5: Record the outcome at the three deciding sites**

Add a private helper to `LiveSessionsBackend`:

```csharp
    // First writer wins: a caller hanging up right after an invitee declined must not rewrite
    // the story. Callers already hold _changeLocks, so the read and the write are atomic.
    private async Task SetOutcome(ChatId chatId, LiveSessionState state, CallOutcome outcome)
    {
        if (state.Outcome != CallOutcome.None)
            return;

        await _redisScope.Set(chatId.Value, state with {
            Outcome = outcome,
            Version = VersionGenerator.NextVersion(state.Version),
        }).ConfigureAwait(false);
    }
```

In `DeclineCall`, inside the lock where `state` is already fetched and `abandoned` computed, next to the existing `SetCallState(…, CallStatus.Declined)` call:

```csharp
            if (abandoned && state is not null)
                await SetOutcome(chatId, state, CallOutcome.Declined).ConfigureAwait(false);
```

In `CancelCall`, inside the lock after `state` is fetched:

```csharp
            await SetOutcome(chatId, state, CallOutcome.Canceled).ConfigureAwait(false);
```

In `ExpireRings`, in the branch that already sets `CallStatus.NoAnswer`:

```csharp
                    await SetOutcome(chatId, current, CallOutcome.NoAnswer).ConfigureAwait(false);
```

Note `CancelCall` sets `Canceled` unconditionally on a still-`None` outcome, including for a call that had connected. That is harmless: Task 5's emission decides by `SessionStartedAt`, not by this field.

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter "FullyQualifiedName~DeclineShouldRecordDeclinedOutcome|FullyQualifiedName~CancelAfterDeclineShouldKeepDeclined|FullyQualifiedName~StartCallShouldRememberHasVideo"`
Expected: PASS, 3 tests.

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/Api/Live/LiveSessionState.cs src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs tests/Chat.IntegrationTests/LiveSessionsTest.cs
git commit -m "feat(call): record how a call ended on its session state"
```

---

### Task 5: Emission and materializing a connected call

**Files:**
- Modify: `src/dotnet/Api/Constants.cs`, `src/dotnet/Chat.Service/Bots.cs`, `src/dotnet/Api/Live/LiveSessionState.cs`, `src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs`
- Test: `tests/Chat.IntegrationTests/CallEntryTest.cs`

**Interfaces:**
- Consumes: `CallEntry`, `ChatEntryKind.Call`, `ChatEntryDiff.CallerId`… from Task 1; `Conversation.IsCall` from Task 3; `LiveSessionState.Outcome`/`HasVideo` from Task 4.
- Produces: `Constants.User.Walle.GetWalleAuthorId(ChatId)`.

- [ ] **Step 1: Write the failing tests**

`tests/Chat.IntegrationTests/CallEntryTest.cs`:

```csharp
using ActualChat.Live;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public sealed class CallEntryTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task UnansweredCallShouldWriteOneNoAnswerEntry()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act
        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);
        await backend.CancelCall(chatId, bob.Id, default);

        // assert
        var entries = await ReadCallEntries(tester, chatId);
        entries.Should().ContainSingle();
        entries[0].Outcome.Should().Be(CallOutcome.Canceled);
        entries[0].CallerId.Should().Be(bob.Id);
        entries[0].InviteeIds.Should().Equal(alice.Id);
    }

    [Fact]
    public async Task AnsweredCallShouldWriteEndedAndMaterializeACallConversation()
    {
        // The case the whole Ended outcome exists for: transcription is off, so nothing else
        // would remain in the chat once the session closes.
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        var conversations = tester.AppServices.GetRequiredService<IConversationsBackend>();

        // act
        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);
        await backend.AcceptCall(chatId, alice.Id, default);
        await backend.LeaveCall(chatId, alice.Id, default);
        await backend.LeaveCall(chatId, bob.Id, default);

        // assert
        var entries = await ReadCallEntries(tester, chatId);
        entries.Should().ContainSingle();
        entries[0].Outcome.Should().Be(CallOutcome.Ended);

        var conversation = await conversations.Get(
            ConversationId.New(chatId, entries[0].Id.LocalId), default);
        conversation.Should().NotBeNull();
        conversation!.IsCall.Should().BeTrue();
    }

    [Fact]
    public async Task CallerHangingUpAnAnsweredCallShouldBeEndedNotCanceled()
    {
        // CancelCall is also the caller's hang-up on a connected call. The split is decided by
        // SessionStartedAt, not by the recorded outcome.
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act
        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);
        await backend.AcceptCall(chatId, alice.Id, default);
        await backend.CancelCall(chatId, bob.Id, default);

        // assert
        var entries = await ReadCallEntries(tester, chatId);
        entries.Should().ContainSingle();
        entries[0].Outcome.Should().Be(CallOutcome.Ended);
    }

    [Fact]
    public async Task GroupCallShouldWriteNoEntry()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var bob = await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        var author = await tester.AppServices.GetRequiredService<IAuthors>()
            .GetOwn(tester.Session, chatId, default);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();

        // act
        await backend.StartCall(chatId, author!.Id, ApiArray<AuthorId>.Empty, false, default);
        await backend.CancelCall(chatId, author.Id, default);

        // assert
        (await ReadCallEntries(tester, chatId)).Should().BeEmpty();
    }

    // Private methods

    private static async Task<(ChatId ChatId, AuthorFull Bob, AuthorFull Alice)> NewPeerChat(
        IWebTester tester)
    {
        var bob = await tester.SignInAsUniqueBob();
        var alice = await tester.SignInAsUniqueAlice();
        await tester.SignIn(bob.User);
        var chatId = PeerChatId.New(bob.Id, alice.Id).ToChatId();
        var authors = tester.AppServices.GetRequiredService<IAuthorsBackend>();
        return (chatId,
            await authors.EnsureJoined(chatId, bob.Id, default),
            await authors.EnsureJoined(chatId, alice.Id, default));
    }

    private static async Task<IReadOnlyList<CallEntry>> ReadCallEntries(IWebTester tester, ChatId chatId)
    {
        var chats = tester.AppServices.GetRequiredService<IChats>();
        var entries = await chats.ReadReverse(tester.Session, chatId, default)
            .Take(50)
            .ToListAsync();
        return entries.OfType<CallEntry>().ToList();
    }
}
```

Mirror `ChatBlockTest.cs` for the peer-chat arrange block if `EnsureJoined` differs here.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter FullyQualifiedName~CallEntryTest`
Expected: FAIL — no entries are written.

- [ ] **Step 3: Make Wall-E's author id reachable from Streaming.Service**

`Streaming.Service` does not reference `Chat.Service`, where `Bots` lives. Add the helper beside `Sherlock.GetSherlockAuthorId`, which is the existing precedent, in `src/dotnet/Api/Constants.cs`:

```csharp
        public static class Walle
        {
            public static readonly UserId UserId = UserId.Parse("walle");
            public static readonly long AuthorLocalId = -1;
            public static readonly string Name =  "Wall-E";
            public static readonly string Picture = "https://api.dicebear.com/7.x/bottts/svg?seed=12";

            public static AuthorId GetWalleAuthorId(ChatId chatId)
                => AuthorId.New(chatId, AuthorLocalId);
        }
```

and make `Bots` delegate rather than duplicate, in `src/dotnet/Chat.Service/Bots.cs`:

```csharp
    public static AuthorId GetWalleId(ChatId chatId)
        => Constants.User.Walle.GetWalleAuthorId(chatId);
```

- [ ] **Step 4: Mark a materialized call conversation**

In `src/dotnet/Api/Live/LiveSessionState.cs`, in `ToMaterializedConversation()`'s initializer:

```csharp
            IsCall = IsCall,
```

- [ ] **Step 5: Emit, and let a connected call materialize**

In `LiveSessionsBackend.CloseAndMaterialize`, replace the early-returning `state.IsCall` branch with:

```csharp
        if (state.IsCall) {
            // Stop any ring still going on an invitee's device before the session goes away.
            var invitees = (await SafeGetInvites(state.ChatId).ConfigureAwait(false))
                .Values.Where(i => i is not null).Select(i => i!.InviteeId).ToList();
            if (invitees.Count > 0)
                await DismissRing(state.RingConversationId, invitees, cancellationToken).ConfigureAwait(false);
            if (state.ChatId.Kind == ChatKind.Peer)
                await WriteCallEntry(state, invitees, cancellationToken).ConfigureAwait(false);
            // A call that never connected has no conversation; one that did is materialized below,
            // and unlike a transcript session it has no title to gate on - the card is the point.
            if (state.SessionStartedAt is not null)
                await Commander
                    .Call(new ConversationBackend_Materialize(state.ToMaterializedConversation()), true, cancellationToken)
                    .ConfigureAwait(false);
            await Close(state.ChatId, cancellationToken).ConfigureAwait(false);
            return;
        }
```

and add the writer:

```csharp
    private async Task WriteCallEntry(
        LiveSessionState state, IReadOnlyList<AuthorId> invitees, CancellationToken cancellationToken)
    {
        // A call that connected is Ended whichever button ended it - CancelCall is also how a
        // caller hangs up - so the outcome recorded during the ring only decides a failed call.
        var outcome = state.SessionStartedAt is not null ? CallOutcome.Ended : state.Outcome;
        if (outcome == CallOutcome.None)
            return;

        var chatId = state.ChatId;
        var callerId = state.Host ?? state.AuthorIds[0];
        var caller = await AuthorsBackend.Get(chatId, callerId, RequestedAuthorKind.Full, cancellationToken)
            .ConfigureAwait(false);
        var callerName = caller?.Avatar.Name.NullIfEmpty() ?? MentionMarkup.NotAvailableName;
        var command = new ChatsBackend_ChangeEntry(
            ChatEntryId.New(chatId, 0),
            null,
            Change.Create(new ChatEntryDiff {
                Kind = ChatEntryKind.Call,
                AuthorId = Constants.User.Walle.GetWalleAuthorId(chatId),
                CallerId = callerId,
                CallerName = callerName,
                Outcome = outcome,
                InviteeIds = invitees.ToApiArray(),
                HasVideo = state.HasVideo,
            }));
        await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);
    }
```

If `LiveSessionsBackend` has no `AuthorsBackend` yet, add it the way its other backends are resolved in the same class.

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter FullyQualifiedName~CallEntryTest`
Expected: PASS, 4 tests.

- [ ] **Step 7: Run the existing call tests for regressions**

Run: `dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter "FullyQualifiedName~LiveSessionsTest|FullyQualifiedName~CallNotificationFlowTest|FullyQualifiedName~ConversationCacheTest"`
Expected: PASS. `ConversationCacheTest` is the one most likely to notice a newly materialized conversation.

- [ ] **Step 8: Commit**

```bash
git add src/dotnet/Api/Constants.cs src/dotnet/Chat.Service/Bots.cs src/dotnet/Api/Live/LiveSessionState.cs src/dotnet/Streaming.Service/Backend/LiveSessionsBackend.cs tests/Chat.IntegrationTests/CallEntryTest.cs
git commit -m "feat(call): write a call outcome entry when a peer call closes"
```

---

### Task 6: Neutral text and its localization

**Files:**
- Modify: `src/dotnet/Api/Chat/Markup/SystemEntryMarkupBuilder.cs`, `src/dotnet/UI.Blazor.App/Services/LocalizedSystemEntryMarkupBuilder.cs`, `src/dotnet/Localization/Resources/Strings.*.json`, `tests/Chat.UI.Blazor.UnitTests/SystemEntryLocalizationTest.cs`

**Interfaces:**
- Consumes: `CallEntry`, `CallOutcome` from Task 1.
- Produces: catalog keys `SystemEntry_CallNoAnswer`, `SystemEntry_CallDeclined`, `SystemEntry_CallCanceled`, `SystemEntry_CallEnded`.

- [ ] **Step 1: Add the samples that make the existing tests fail**

In `tests/Chat.UI.Blazor.UnitTests/SystemEntryLocalizationTest.cs`, extend `Entries`:

```csharp
        foreach (var outcome in new[] {
                     CallOutcome.NoAnswer, CallOutcome.Declined, CallOutcome.Canceled, CallOutcome.Ended,
                 })
            yield return new CallEntry {
                // CallerId is non-nullable, so unlike the entries above there is no id-less variant;
                // the null batch simply repeats this sample.
                CallerId = authorId ?? MentionedAuthorId,
                CallerName = AuthorName,
                Outcome = outcome,
            };
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter FullyQualifiedName~SystemEntryLocalizationTest`
Expected: FAIL — `SystemEntryMarkupBuilder` renders `CallEntry` as empty markup, so the "renders no sentence" and "drops the author name" assertions trip.

- [ ] **Step 3: Add the English wording**

In `src/dotnet/Api/Chat/Markup/SystemEntryMarkupBuilder.cs`, add an arm to `Build`:

```csharp
            CallEntry e => BuildCall(e),
```

and the method:

```csharp
    protected virtual Markup BuildCall(CallEntry entry)
    {
        var callerName = entry.CallerName.NullIfEmpty() ?? SomeoneName;
        return new MarkupSeq(
            new AuthorMention(MentionRef.NewAuthor(entry.CallerId), callerName),
            new PlainTextMarkup(entry.Outcome switch {
                CallOutcome.NoAnswer => " called. No answer.",
                CallOutcome.Declined => " called. Declined.",
                CallOutcome.Canceled => " called. Canceled.",
                _ => " called.",
            }));
    }
```

- [ ] **Step 4: Add the localized override**

In `src/dotnet/UI.Blazor.App/Services/LocalizedSystemEntryMarkupBuilder.cs`:

```csharp
    protected override Markup BuildCall(CallEntry entry)
    {
        var callerName = entry.CallerName.NullIfEmpty() ?? SomeoneName;
        return new MarkupSeq(
            new AuthorMention(MentionRef.NewAuthor(entry.CallerId), callerName),
            new PlainTextMarkup(entry.Outcome switch {
                CallOutcome.NoAnswer => L.SystemEntry_CallNoAnswer,
                CallOutcome.Declined => L.SystemEntry_CallDeclined,
                CallOutcome.Canceled => L.SystemEntry_CallCanceled,
                _ => L.SystemEntry_CallEnded,
            }));
    }
```

- [ ] **Step 5: Add the keys to every hand-written catalog**

In `src/dotnet/Localization/Resources/Strings.en.json`, beside the existing `SystemEntry_*` keys:

```json
  "SystemEntry_CallNoAnswer": " called. No answer.",
  "SystemEntry_CallDeclined": " called. Declined.",
  "SystemEntry_CallCanceled": " called. Canceled.",
  "SystemEntry_CallEnded": " called.",
```

Then translate the same four keys into every other hand-written catalog, following `docs/i18n.md`. `AppLocalizationTest` requires every catalog to define exactly the English key set.

- [ ] **Step 6: Regenerate the derived catalogs**

```bash
scripts/derive-bcms.cmd
scripts/derive-max.cmd
scripts/derive-bcms.cmd --check
scripts/derive-max.cmd --check
```

Expected: the two `--check` runs report no drift.

- [ ] **Step 7: Run the localization tests**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter "FullyQualifiedName~SystemEntryLocalizationTest|FullyQualifiedName~AppLocalizationTest"`
Expected: PASS. The English catalog must byte-match what `SystemEntryMarkupBuilder.Default` renders, so Steps 3 and 5 have to agree exactly.

- [ ] **Step 8: Commit**

```bash
git add src/dotnet/Api/Chat/Markup/SystemEntryMarkupBuilder.cs src/dotnet/UI.Blazor.App/Services/LocalizedSystemEntryMarkupBuilder.cs src/dotnet/Localization/Resources tests/Chat.UI.Blazor.UnitTests/SystemEntryLocalizationTest.cs
git commit -m "feat(call): word a call entry for previews and notifications"
```

---

### Task 7: The failed-outcome card

**Files:**
- Create: `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Call/CallCardFormat.cs`, `.../Call/CallMessageView.razor`, `.../Call/call-message.css`
- Modify: `src/dotnet/UI.Blazor.App/Components/ChatView/Items/ChatEntryMessageView.razor`, `src/dotnet/Localization/Resources/Strings.*.json`
- Test: `tests/Chat.UI.Blazor.UnitTests/CallCardFormatTest.cs`

**Interfaces:**
- Consumes: `CallEntry`, `CallOutcome` from Task 1.
- Produces: `CallCardFormat.Get(CallOutcome outcome, bool isCaller, IStringLocalizer l)` returning `(string Icon, string Title, string? Hint, bool IsCallBack)`.

- [ ] **Step 1: Write the failing test**

`tests/Chat.UI.Blazor.UnitTests/CallCardFormatTest.cs`:

```csharp
using ActualChat.Localization;
using ActualChat.UI.Blazor.App.Components;
using Microsoft.Extensions.Localization;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class CallCardFormatTest
{
    [Theory]
    [InlineData(CallOutcome.NoAnswer, true, "icon-call-out", false)]
    [InlineData(CallOutcome.NoAnswer, false, "icon-phone-missed", true)]
    [InlineData(CallOutcome.Declined, true, "icon-phone-off", false)]
    [InlineData(CallOutcome.Declined, false, "icon-phone-off", false)]
    [InlineData(CallOutcome.Canceled, true, "icon-phone-off", false)]
    [InlineData(CallOutcome.Canceled, false, "icon-phone-missed", true)]
    public void EveryCellOfTheWordingTableShouldResolve(
        CallOutcome outcome, bool isCaller, string expectedIcon, bool expectedCallBack)
    {
        // arrange
        var l = NewLocalizer();

        // act
        var (icon, title, hint, isCallBack) = CallCardFormat.Get(outcome, isCaller, l);

        // assert
        icon.Should().Be(expectedIcon);
        title.Should().NotBeNullOrWhiteSpace();
        title.Should().NotContain("Call_Entry_", "the key must resolve, not render as itself");
        isCallBack.Should().Be(expectedCallBack);
        if (expectedCallBack)
            hint.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void MissedAndOutgoingShouldReadDifferently()
    {
        // The two sides of one stored row must not collapse into the same sentence.
        var l = NewLocalizer();
        var caller = CallCardFormat.Get(CallOutcome.NoAnswer, true, l);
        var callee = CallCardFormat.Get(CallOutcome.NoAnswer, false, l);
        caller.Title.Should().NotBe(callee.Title);
    }

    private static IStringLocalizer NewLocalizer()
        => new TestStringLocalizer(StringCatalogs.LoadStrings(Languages.English)!, Languages.English);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter FullyQualifiedName~CallCardFormatTest`
Expected: FAIL — `CallCardFormat` does not exist.

- [ ] **Step 3: Add the card strings**

In `src/dotnet/Localization/Resources/Strings.en.json`, beside the other `Call_*` keys:

```json
  "Call_Entry_Outgoing": "Outgoing call",
  "Call_Entry_Missed": "Missed call",
  "Call_Entry_Declined": "Declined call",
  "Call_Entry_Canceled": "Canceled call",
  "Call_Entry_Ended": "Call ended",
  "Call_Entry_NoAnswer": "No answer",
  "Call_Entry_TapToCallBack": "Tap to call back",
```

Translate the same seven keys into every other hand-written catalog, then rerun the derived generators as in Task 6 Step 6.

- [ ] **Step 4: Write the mapping**

`src/dotnet/UI.Blazor.App/Components/ChatView/Items/Call/CallCardFormat.cs`:

```csharp
using ActualChat.Localization;
using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor.App.Components;

/// <summary>
/// The one place the outcome-by-reader wording table lives. Both render paths call it — the card
/// for a call that never connected, and the conversation item's call mode for one that did.
/// </summary>
public static class CallCardFormat
{
    public static (string Icon, string Title, string? Hint, bool IsCallBack) Get(
        CallOutcome outcome, bool isCaller, IStringLocalizer l)
        => outcome switch {
            CallOutcome.NoAnswer when isCaller =>
                ("icon-call-out", l.Call_Entry_Outgoing, l.Call_Entry_NoAnswer, false),
            CallOutcome.NoAnswer =>
                ("icon-phone-missed", l.Call_Entry_Missed, l.Call_Entry_TapToCallBack, true),
            CallOutcome.Declined =>
                ("icon-phone-off", l.Call_Entry_Declined, null, false),
            CallOutcome.Canceled when isCaller =>
                ("icon-phone-off", l.Call_Entry_Canceled, null, false),
            CallOutcome.Canceled =>
                ("icon-phone-missed", l.Call_Entry_Missed, l.Call_Entry_TapToCallBack, true),
            _ => ("icon-phone-call", l.Call_Entry_Ended, null, false),
        };
}
```

- [ ] **Step 5: Run the test**

Run: `dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj --filter FullyQualifiedName~CallCardFormatTest`
Expected: PASS, 7 tests.

- [ ] **Step 6: Write the card**

`src/dotnet/UI.Blazor.App/Components/ChatView/Items/Call/CallMessageView.razor`:

```razor
@namespace ActualChat.UI.Blazor.App.Components
@inherits ComputedStateComponent<AppUIHub, bool>
@{
    var isCaller = State.Value;
    var (icon, title, hint, isCallBack) = CallCardFormat.Get(Entry.Outcome, isCaller, L);
    var avatarIds = new[] { Entry.CallerId }.Concat(Entry.InviteeIds).ToApiArray();
    var startsAt = DateTimeConverter.ToLocalTime(Entry.BeginsAt).ToString("t", DateFormatter);
}

<div class="call-message">
    <div class="c-cm-title">
        <i class="@icon"></i>
        <span class="c-cm-name">@title</span>
        @if (hint is not null) {
            @if (isCallBack) {
                <button type="button" class="c-cm-hint action" @onclick="@OnCallBack">@hint</button>
            } else {
                <span class="c-cm-hint">@hint</span>
            }
        }
    </div>
    <div class="c-cm-meta-row">
        <AuthorCircleGroup
            Class="c-cm-authors"
            AuthorIds="@avatarIds"
            Size="@SquareSize.Size5"
            MaxCount="4"
            ShowRing="false"/>
        <div class="c-cm-meta">@startsAt</div>
    </div>
</div>

@code {
    private LiveSessionUI LiveSessionUI => Hub.LiveSessionUI;
    private IChats Chats => Hub.Chats;

    [Parameter, EditorRequired] public CallEntry Entry { get; set; } = null!;

    protected override ComputedState<bool>.Options GetStateOptions()
        => ComputedStateComponent.GetStateOptions(GetType(),
            static t => new ComputedState<bool>.Options() {
                InitialValue = false,
                Category = GetStateCategory(t),
            });

    protected override async Task<bool> ComputeState(CancellationToken cancellationToken) {
        var chat = await Chats.Get(Session, Entry.ChatId, cancellationToken).ConfigureAwait(false);
        return chat?.Rules.Author?.Id == Entry.CallerId;
    }

    private Task OnCallBack()
        => LiveSessionUI.StartCall(
            Entry.ChatId, new[] { Entry.CallerId }.ToApiArray(), Entry.HasVideo, default);
}
```

- [ ] **Step 7: Write the styles**

`src/dotnet/UI.Blazor.App/Components/ChatView/Items/Call/call-message.css`, following the shape of `c-live-card` in `../Conversation/conversation.css`:

```css
.call-message {
    @apply flex flex-col gap-y-2;
    @apply mx-2 my-1 p-3;
    @apply rounded-lg bg-01;
}
.call-message .c-cm-title {
    @apply flex flex-row items-center gap-x-2;
}
.call-message .c-cm-title > i {
    @apply text-xl text-danger;
}
.call-message .c-cm-name {
    @apply flex-1 text-title-1 text-02;
}
.call-message .c-cm-hint {
    @apply text-sm text-03;
}
.call-message .c-cm-hint.action {
    @apply text-primary;
}
.call-message .c-cm-meta-row {
    @apply flex flex-row items-center gap-x-2;
}
.call-message .c-cm-meta {
    @apply text-sm text-03;
}
```

Check the utility class names against `conversation.css` before committing — reuse whatever it uses for the same roles rather than inventing new ones.

- [ ] **Step 8: Route the entry to the card**

In `src/dotnet/UI.Blazor.App/Components/ChatView/Items/ChatEntryMessageView.razor`, immediately before the existing `@if (isSystem) {` block at line 70:

```razor
@if (entry is CallEntry callEntry) {
    <div class="@cls" data-chat-entry-id="@(entry.Id.Value)">
        <CallMessageView Entry="@callEntry"/>
    </div>
    return;
}
```

- [ ] **Step 9: Build the app project**

Run: `dotnet build src/dotnet/UI.Blazor.App/UI.Blazor.App.csproj`
Expected: succeeds.

- [ ] **Step 10: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Components/ChatView/Items/Call src/dotnet/UI.Blazor.App/Components/ChatView/Items/ChatEntryMessageView.razor src/dotnet/Localization/Resources tests/Chat.UI.Blazor.UnitTests/CallCardFormatTest.cs
git commit -m "feat(call): show a card for a call that never connected"
```

---

### Task 8: `Ended` through the conversation card

**Files:**
- Modify: `src/dotnet/UI.Blazor.App/Services/ChatUI.Tiles.cs`, `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/ConversationHeaderView.razor`, `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/ConversationMessageView.razor`
- Test: `tests/Chat.UI.Blazor.IntegrationTests/CallConversationCardTest.cs`

**Interfaces:**
- Consumes: `CallEntry`/`CallOutcome` from Task 1, `Conversation.IsCall` from Task 3, `CallCardFormat` from Task 7.

- [ ] **Step 1: Write the failing test**

`tests/Chat.UI.Blazor.IntegrationTests/CallConversationCardTest.cs`. The fixture, the `Tester` property and the way tiles are read all follow `LiveConversationDisplayTest.cs` in the same folder:

```csharp
using ActualChat.Live;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public sealed class CallConversationCardTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    private BlazorTester Tester => field ??= AppHost.NewBlazorTester(Out);

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task AnEndedCallShouldProduceOneCardNotTwo()
    {
        // The Ended entry is the anchor the conversation card hangs on, so both are in the tile.
        // Rendering both would show the same call twice.

        // arrange
        var bob = await Tester.SignInAsUniqueBob();
        var alice = await Tester.SignInAsUniqueAlice();
        await Tester.SignIn(bob.User);
        var chatId = PeerChatId.New(bob.Id, alice.Id).ToChatId();
        var authors = AppHost.Services.GetRequiredService<IAuthorsBackend>();
        var bobAuthor = await authors.EnsureJoined(chatId, bob.Id, CancellationToken.None);
        var aliceAuthor = await authors.EnsureJoined(chatId, alice.Id, CancellationToken.None);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();

        // act
        await liveBackend.StartCall(
            chatId, bobAuthor.Id, new[] { aliceAuthor.Id }.ToApiArray(), false, CancellationToken.None);
        await liveBackend.AcceptCall(chatId, aliceAuthor.Id, CancellationToken.None);
        await liveBackend.LeaveCall(chatId, aliceAuthor.Id, CancellationToken.None);
        await liveBackend.LeaveCall(chatId, bobAuthor.Id, CancellationToken.None);

        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chatId, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        var items = await chatUI.GetChatItems(chatId, query, 0, CancellationToken.None);
        var messages = items.Items.SelectMany(i => i.GetLeafMessages()).ToList();

        // assert
        messages.OfType<ChatEntryMessage>()
            .Should().NotContain(m => m.Entry is CallEntry { Outcome: CallOutcome.Ended },
                "the entry only anchors the card in the lid range; the conversation item is the card");
        messages.Should().Contain(m => m.Conversation is { IsCall: true },
            "a finished call must leave a conversation card behind");
    }
}
```

If `IAuthorsBackend.EnsureJoined` differs in this tree, mirror `tests/Chat.IntegrationTests/ChatBlockTest.cs`, which builds the same peer chat.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Chat.UI.Blazor.IntegrationTests/Chat.UI.Blazor.IntegrationTests.csproj --filter FullyQualifiedName~CallConversationCardTest`
Expected: FAIL — the `Ended` entry is still rendered as a message.

- [ ] **Step 3: Skip the entry in the tile builder**

In `src/dotnet/UI.Blazor.App/Services/ChatUI.Tiles.cs`, directly below the existing unsupported-system-entry skip at line 1206:

```csharp
                // The conversation card is this call's representation; the entry only anchors it
                // in the lid range, which is all a call with no transcript would otherwise have.
                if (e is CallEntry { Outcome: CallOutcome.Ended })
                    continue;
```

- [ ] **Step 4: Give the conversation header a call mode**

`ConversationMessageHeader` already takes both the icon and the title as parameters (`Icon`, defaulting to `"icon-tldr"`, and `Title`), so nothing inside it changes — the callers pass call values instead.

In `src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/ConversationHeaderView.razor`, replace the existing `<ConversationMessageHeader …/>` invocation with:

```razor
@{
    var (callIcon, callTitle, _, _) = CallCardFormat.Get(CallOutcome.Ended, false, L);
}
<ConversationMessageHeader
    Conversation="@conversation"
    Title="@(conversation.IsCall ? TranslatedText.From(callTitle) : m.Title)"
    Icon="@(conversation.IsCall ? callIcon : "icon-tldr")"
    MessageCount="@messageCount"
    ConversationToggleClick="@ToggleShowMessage"
    StartsAt="@startsAt"
    IsSeparated="true"/>
```

Make the same substitution at the second call site, in `ConversationMessageView.razor`'s non-live branch — the `<ConversationMessageHeader …/>` guarded by `!Message.HasSplitHeader`.

A call's title is empty by construction when transcription was off, which is exactly why the title comes from the mapping rather than from the conversation.

- [ ] **Step 4b: Show the duration**

In `ConversationMessageFooter.razor`, which already receives `StartsAt` and `EndsAt`, add the elapsed time when the conversation is a call. Read the component first and follow whatever it already does for the meta row; the value is `EndsAt - StartsAt`, formatted the way the live card formats its own elapsed time.

- [ ] **Step 5: Run the test**

Run: `dotnet test tests/Chat.UI.Blazor.IntegrationTests/Chat.UI.Blazor.IntegrationTests.csproj --filter FullyQualifiedName~CallConversationCardTest`
Expected: PASS.

- [ ] **Step 6: Run the conversation regression tests**

Run: `dotnet test tests/Chat.UI.Blazor.IntegrationTests/Chat.UI.Blazor.IntegrationTests.csproj --filter "FullyQualifiedName~LiveConversationDisplayTest|FullyQualifiedName~ChatUICacheTest"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/dotnet/UI.Blazor.App/Services/ChatUI.Tiles.cs src/dotnet/UI.Blazor.App/Components/ChatView/Items/Conversation/ConversationMessageHeader.razor tests/Chat.UI.Blazor.IntegrationTests/CallConversationCardTest.cs
git commit -m "feat(call): draw a finished call as its conversation card"
```

---

### Task 9: Keep the entry out of an old client's chat list

The tolerance work gave `IChats.GetTile` a filtering twin, but **not** `GetNews`. `ChatNews.LastTextEntry` is a `ChatEntry?`, and `ChatNews.ToSlim` rebuilds it with `entry with { … }`, which preserves the concrete type. So a `CallEntry` reaches a pre-2.19 client through the chat list and kills the whole `ChatNews` payload on deserialization — and `CallEntry` becomes the last entry of a peer chat after every single call.

**Files:**
- Modify: `src/dotnet/Api/Chat/ChatNews.cs`, `src/dotnet/Api.Contracts/Chat/IChats.cs`, `src/dotnet/Chat.Service/Chats.cs`
- Test: `tests/Chat.UnitTests/LegacyTileRoutingTest.cs` (extend)

**Interfaces:**
- Consumes: `ChatEntry.IsKnownTo` and `ApiConstants.LastVersionWithoutUnionTolerance` from the base branch; `CallEntry` from Task 1.
- Produces: `ChatNews.WithoutEntryUnknownTo(Version apiVersion)`; `IChats.GetLegacyNews`.

- [ ] **Step 1: Write the failing test**

Append to `tests/Chat.UnitTests/LegacyTileRoutingTest.cs`:

```csharp
    [Fact]
    public void NewsShouldDropALastEntryAnOldPeerCannotRead()
    {
        // ChatNews carries a ChatEntry to the chat list, and ToSlim keeps its concrete type -
        // so without this an unknown tag reaches a peer that dies on it.
        // arrange
        var chatId = ChatId.Parse("052w3sgrad");
        var news = new ChatNews(new Range<long>(0, 10), new CallEntry(ChatEntryId.New(chatId, 9)) {
            CallerId = AuthorId.New(chatId, 1),
            Outcome = CallOutcome.NoAnswer,
        });

        // act & assert
        news.WithoutEntryUnknownTo(new Version(2, 18)).LastTextEntry.Should().BeNull();
        news.WithoutEntryUnknownTo(new Version(2, 19)).LastTextEntry.Should().NotBeNull();
    }

    [Fact]
    public void NewsShouldKeepALastEntryEveryPeerCanRead()
    {
        var chatId = ChatId.Parse("052w3sgrad");
        var news = new ChatNews(new Range<long>(0, 10), new TextEntry(ChatEntryId.New(chatId, 9)));

        news.WithoutEntryUnknownTo(new Version(2, 18)).Should().BeSameAs(news);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Chat.UnitTests/Chat.UnitTests.csproj --filter FullyQualifiedName~LegacyTileRoutingTest`
Expected: FAIL — `WithoutEntryUnknownTo` does not exist.

- [ ] **Step 3: Add the filter**

In `src/dotnet/Api/Chat/ChatNews.cs`, mirroring `ChatTile.WithoutEntriesUnknownTo`:

```csharp
    // The entry is dropped rather than replaced with a stand-in: the preview line is composed on
    // the client, in the viewer's language, so a server-side substitute could only be English.
    // A chat whose last entry a peer can't read reads as one with no preview - the same shape a
    // removed last entry already produces.
    public ChatNews WithoutEntryUnknownTo(Version apiVersion)
        => LastTextEntry is { } entry && !ChatEntry.IsKnownTo(entry, apiVersion)
            ? this with { LastTextEntry = null }
            : this;
```

- [ ] **Step 4: Route old peers to a filtering twin**

In `src/dotnet/Api.Contracts/Chat/IChats.cs`, `GetNews` already steps aside for v2.12- peers. Add a second band, so it also steps aside for v2.18- ones:

```csharp
    [LegacyName("GetNews_NewUnused", ApiConstants.LastVersionWithoutUnionTolerance)]
```

and declare the twin beside it:

```csharp
    [ComputeMethod(MinCacheDuration = 10), RemoteComputeMethod(MinCacheDuration = 300)]
    [LegacyName("GetLegacyNews_NewUnused", "2.12.9999")]
    [LegacyName(nameof(GetNews), ApiConstants.LastVersionWithoutUnionTolerance)]
    [Obsolete("2026.09: Use GetNews - this one only drops a last entry a pre-2.19 client can't read.")]
    Task<ChatNews?> GetLegacyNews(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken);
```

Copy the `[ComputeMethod]`/`[RemoteComputeMethod]` attributes from `GetNews` verbatim rather than from here — this plan may lag the real ones.

The `GetLegacyNews_NewUnused` entry keeps the v2.12- band resolved by declaration. `RpcMethodResolver` would otherwise settle it by the lowest-`MaxVersion`-wins rule at `RpcMethodResolver.cs:131`, which happens to give the right answer but is not something to lean on.

- [ ] **Step 5: Implement it**

In `src/dotnet/Chat.Service/Chats.cs`, beside `GetLegacyTile`:

```csharp
    // [ComputeMethod]
    [Obsolete("2026.09: Use GetNews - this one only drops a last entry a pre-2.19 client can't read.")]
    public virtual async Task<ChatNews?> GetLegacyNews(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var news = await GetNews(session, chatId, cancellationToken).ConfigureAwait(false);
        return news?.WithoutEntryUnknownTo(LastToleratedApiVersion);
    }
```

`LastToleratedApiVersion` is the field `GetLegacyTile` already uses in this class.

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/Chat.UnitTests/Chat.UnitTests.csproj --filter FullyQualifiedName~LegacyTileRoutingTest`
Expected: PASS.

- [ ] **Step 7: Verify the RPC wiring starts**

Run: `dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj --filter FullyQualifiedName~CallEntryTest`
Expected: PASS. A `[LegacyName]` collision throws at service-registry build time (`RpcMethodResolver.cs:127`), so if the two bands are wrong, the host fails to start and every test in the collection fails — that is the signal to read for.

- [ ] **Step 8: Commit**

```bash
git add src/dotnet/Api/Chat/ChatNews.cs src/dotnet/Api.Contracts/Chat/IChats.cs src/dotnet/Chat.Service/Chats.cs tests/Chat.UnitTests/LegacyTileRoutingTest.cs
git commit -m "fix(chat): keep an unreadable last entry out of an old client's chat list"
```

---

### Task 10: Legacy-tile filtering and a full pass

**Files:**
- Modify: `tests/Chat.UnitTests/LegacyTileRoutingTest.cs`

- [ ] **Step 1: Write the failing test**

Add to `tests/Chat.UnitTests/LegacyTileRoutingTest.cs`, following the shape of the cases already there:

```csharp
    [Fact]
    public void CallEntryShouldBeFilteredForAPreTolerancePeer()
    {
        // Declaring the release in UnionTagSinceVersions is what keeps the entry away from a peer
        // that would fail on its tag. Without the declaration this passes for the wrong reason.
        var chatId = ChatId.Parse("052w3sgrad");
        var entry = new CallEntry(ChatEntryId.New(chatId, 1)) {
            CallerId = AuthorId.New(chatId, 1),
            Outcome = CallOutcome.NoAnswer,
        };

        ChatEntry.IsKnownTo(entry, new Version(2, 18)).Should().BeFalse();
        ChatEntry.IsKnownTo(entry, new Version(2, 19)).Should().BeTrue();
    }
```

- [ ] **Step 2: Run it**

Run: `dotnet test tests/Chat.UnitTests/Chat.UnitTests.csproj --filter FullyQualifiedName~LegacyTileRoutingTest`
Expected: PASS — Task 1 Step 5 already declared the release. If it fails, that declaration is missing or wrong.

- [ ] **Step 3: Run every affected suite**

```bash
dotnet test tests/Chat.UnitTests/Chat.UnitTests.csproj
dotnet test tests/Chat.UI.Blazor.UnitTests/Chat.UI.Blazor.UnitTests.csproj
dotnet test tests/Chat.IntegrationTests/Chat.IntegrationTests.csproj
dotnet test tests/Chat.UI.Blazor.IntegrationTests/Chat.UI.Blazor.IntegrationTests.csproj
```

Expected: all pass. Report any failure rather than working around it.

- [ ] **Step 4: Commit**

```bash
git add tests/Chat.UnitTests/LegacyTileRoutingTest.cs
git commit -m "test(call): assert CallEntry stays away from pre-2.19 peers"
```

---

## Notes for the executor

- **The release number is a decision, not a constant.** `[101] = new (2, 19)` assumes this ships alongside the tolerance work in 2.19. If the branch slips, that number moves with the release — check `version.json` before finishing.
- **The peer-chat test arrange block** is the most likely thing in this plan to not compile as written; `tests/Chat.IntegrationTests/ChatBlockTest.cs` is the reference for building a peer chat with two signed-in users.
- **Do not rebase onto `origin/dev`.** The two commits under this branch (`feat/forward-compatible-unions`) are what make the new union tag safe.
