using ActualChat.Chat.Module;
using ActualChat.Contacts;
using ActualChat.Localization;
using ActualChat.Streaming;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.Video;
using Bunit;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

/// <summary>
/// Tests the backend video state transitions that drive video panel visibility
/// in ChatHeader.razor. The panel is shown when IsRecording=true OR
/// IsAnyoneVideoStreaming=true, and hidden when both are false.
/// </summary>
[Collection(nameof(ChatUICollection))]
public class ChatVideoUIStateTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task ShouldReportIsAnyoneStreamingViaBackend()
    {
        // Arrange: create two users and a chat
        await using var bob = AppHost.NewWebClientTester(Out);
        await using var alice = AppHost.NewWebClientTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();

        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);

        var backend = AppHost.Services.GetRequiredService<ILiveVideoBackend>();
        var frontend = AppHost.Services.GetRequiredService<ILiveVideoStreams>();
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        bobAuthor.Should().NotBeNull();

        // Initially no one is streaming
        var streams = await backend.List(chatId, CancellationToken.None);
        streams.Should().BeEmpty();

        // Register Bob's video stream
        var streamId = StreamId.New(AppHost.Services.MeshWatcher().ThisNode.Ref);
        var streamInfo = new VideoStreamInfo(
            streamId,
            chatId,
            bobAuthor!.Id,
            new VideoFormat { Codec = "avc1", Size = new Size2D(640, 480) },
            Clocks.SystemClock.Now);

        await backend.Register(chatId, streamInfo, CancellationToken.None);

        // Check via frontend List
        var frontendStreams = await frontend.List(alice.Session, chatId, CancellationToken.None);
        frontendStreams.Should().HaveCount(1);
        frontendStreams[0].AuthorId.Should().Be(bobAuthor.Id);

        // Unregister the stream
        await backend.Unregister(chatId, streamId, CancellationToken.None);

        // Now no one should be streaming
        frontendStreams = await frontend.List(alice.Session, chatId, CancellationToken.None);
        frontendStreams.Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldInvalidateComputedOnStreamChange()
    {
        // Arrange
        await using var bob = AppHost.NewWebClientTester(Out);
        await bob.SignInAsUniqueBob();

        var (chatId, _) = await bob.CreateChat(true);

        var backend = AppHost.Services.GetRequiredService<ILiveVideoBackend>();
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        bobAuthor.Should().NotBeNull();

        // Capture initial computed value
        var computed = await Computed.Capture(
            () => backend.List(chatId, CancellationToken.None));
        computed.Value.Should().BeEmpty();
        computed.IsConsistent().Should().BeTrue();

        // Register a video stream — computed should become inconsistent
        var streamId = StreamId.New(AppHost.Services.MeshWatcher().ThisNode.Ref);
        var streamInfo = new VideoStreamInfo(
            streamId,
            chatId,
            bobAuthor!.Id,
            new VideoFormat { Codec = "avc1", Size = new Size2D(640, 480) },
            Clocks.SystemClock.Now);

        await backend.Register(chatId, streamInfo, CancellationToken.None);
        computed.IsConsistent().Should().BeFalse();

        // Update — should reflect new state
        computed = await computed.Update(CancellationToken.None);
        computed.Value.Should().HaveCount(1);

        // Unregister — should invalidate again
        await backend.Unregister(chatId, streamId, CancellationToken.None);
        computed.IsConsistent().Should().BeFalse();

        computed = await computed.Update(CancellationToken.None);
        computed.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldMarkVideoUnavailableForNotesChat()
    {
        // arrange
        await using var appHost = await NewAppHost("notes-chat-video", options => options with {
            ChatDbInitializerOptions = ChatDbInitializer.Options.Default with {
                AddNotesChat = true,
            },
        });
        await using var tester = appHost.NewBlazorTester(Out);
        await tester.SignInAsNew("NotesVideo");
        var contacts = tester.AppServices.GetRequiredService<IContacts>();
        var chatsBackend = tester.AppServices.GetRequiredService<IChatsBackend>();
        var chatVideoUI = tester.ScopedAppServices.GetRequiredService<ChatVideoUI>();
        Chat? notesChat = null;

        await TestWait.When(async ct => {
            var chats = await (await contacts.ListIds(tester.Session, null, ct).ConfigureAwait(false))
                .Select(x => chatsBackend.Get(x.ChatId, ct))
                .Collect(ct)
                .ConfigureAwait(false);
            notesChat = chats
                .SkipNullItems()
                .FirstOrDefault(c => c.IsNotes);
            notesChat.Should().NotBeNull();
        });

        var (regularChatId, _) = await tester.CreateChat(false);
        var regularChat = await tester.Chats.Get(tester.Session, regularChatId, CancellationToken.None).Require();

        // act
        var isNotesVideoAvailable = await chatVideoUI.IsVideoAvailable(notesChat!.Id, CancellationToken.None);
        var isRegularVideoAvailable = await chatVideoUI.IsVideoAvailable(regularChatId, CancellationToken.None);
        var isNotesVideoAvailableNonComputed = chatVideoUI.IsVideoAvailableNonComputed(notesChat);
        var isRegularVideoAvailableNonComputed = chatVideoUI.IsVideoAvailableNonComputed(regularChat);
        var isNotesVideoEnabled = await chatVideoUI.IsVideoAvailable(notesChat!.Id, CancellationToken.None);
        var isRegularVideoEnabled = await chatVideoUI.IsVideoAvailable(regularChatId, CancellationToken.None);

        // assert
        isNotesVideoAvailable.Should().BeFalse();
        isNotesVideoAvailableNonComputed.Should().BeFalse();
        isNotesVideoEnabled.Should().BeFalse();
        isRegularVideoAvailable.Should().BeTrue();
        isRegularVideoAvailableNonComputed.Should().BeTrue();
        isRegularVideoEnabled.Should().BeTrue();
    }

    /// <summary>
    /// Simulates the ChatHeader.razor logic: when IsRecording=false AND
    /// IsAnyoneVideoStreaming=false, the panel should hide; when either is true,
    /// panel should show. This tests the backend state that drives those conditions.
    /// </summary>
    [Fact]
    public async Task ShouldDetermineVideoPanelVisibility()
    {
        // Arrange
        await using var bob = AppHost.NewWebClientTester(Out);
        await using var alice = AppHost.NewWebClientTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();

        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);

        var backend = AppHost.Services.GetRequiredService<ILiveVideoBackend>();
        var frontend = AppHost.Services.GetRequiredService<ILiveVideoStreams>();
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        bobAuthor.Should().NotBeNull();

        // Helper: simulate ChatHeader's panel visibility check
        // Panel shows when isRecording OR isAnyoneVideoStreaming
        async Task<bool> ShouldShowPanel(bool isRecording) {
            var streams = await frontend.List(alice.Session, chatId, CancellationToken.None);
            var isAnyoneVideoStreaming = streams.Count > 0;
            return isRecording || isAnyoneVideoStreaming;
        }

        // Initially: not recording, no streams → panel hidden
        var show = await ShouldShowPanel(isRecording: false);
        show.Should().BeFalse("no recording and no streams → panel should be hidden");

        // Bob starts recording (simulated by flag) → panel shown
        show = await ShouldShowPanel(isRecording: true);
        show.Should().BeTrue("recording is on → panel should show");

        // Bob also starts a video stream
        var streamId = StreamId.New(AppHost.Services.MeshWatcher().ThisNode.Ref);
        var streamInfo = new VideoStreamInfo(
            streamId,
            chatId,
            bobAuthor!.Id,
            new VideoFormat { Codec = "avc1", Size = new Size2D(640, 480) },
            Clocks.SystemClock.Now);
        await backend.Register(chatId, streamInfo, CancellationToken.None);

        // Recording + streams → panel shown
        show = await ShouldShowPanel(isRecording: true);
        show.Should().BeTrue("recording + streams → panel should show");

        // Bob stops recording but stream still active → panel stays
        show = await ShouldShowPanel(isRecording: false);
        show.Should().BeTrue("streams still active → panel should stay visible");

        // Stream ends → panel hidden
        await backend.Unregister(chatId, streamId, CancellationToken.None);
        show = await ShouldShowPanel(isRecording: false);
        show.Should().BeFalse("no recording and no streams → panel should hide");
    }

    [Fact]
    public async Task RecorderErrorCodeShouldReachUserLocalized()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var chatVideoUI = tester.ScopedAppServices.GetRequiredService<ChatVideoUI>();
        var l = tester.ScopedAppServices.GetRequiredService<IStringLocalizer>();
        var kind = VideoSourceKind.Camera;

        // act
        chatVideoUI.OnRecordingError(VideoRecorderError.CameraUnavailable, "FaceTime HD", "raw", kind);
        var named = await chatVideoUI.GetLastVideoRecorderError(kind);
        chatVideoUI.OnRecordingError(VideoRecorderError.CameraUnavailable, null, "raw", kind);
        var unnamed = await chatVideoUI.GetLastVideoRecorderError(kind);
        chatVideoUI.OnRecordingError(VideoRecorderError.RestartRequired, null, "raw", kind);
        var restart = await chatVideoUI.GetLastVideoRecorderError(kind);
        chatVideoUI.OnRecordingError(VideoRecorderError.Unknown, null, "Requested device not found", kind);
        var unknown = await chatVideoUI.GetLastVideoRecorderError(kind);

        // assert
        named.Should().Be(l.Video_CameraUnavailableNamed_Format("FaceTime HD"));
        unnamed.Should().Be(l.Call_CameraIsUnavailable);
        restart.Should().Be(l.Video_RestartRequired);
        unknown.Should().Be("Requested device not found", "browser wording we don't own stays untranslated");
    }

    [Fact(Timeout = 120_000)]
    public async Task RecorderShouldBeDisposedWhenItsCircuitIsGone()
    {
        // arrange
        var js = new BunitJSInterop { Mode = JSRuntimeMode.Loose };
        var recorderJS = js.SetupModule(i => i.Identifier.EndsWith(".VideoRecorder.create"));
        recorderJS.Mode = JSRuntimeMode.Loose;
        recorderJS.SetupVoid("stopRecording")
            .SetException(new JSDisconnectedException("The circuit has disconnected"));
        var appHost = await NewAppHost("video-recorder-circuit-gone", options => options with {
            ConfigureServices = (_, services) => services.Replace(ServiceDescriptor.Scoped(_ => js.JSRuntime)),
        });
        await using var _1 = appHost;
        await using var alice = appHost.NewWebClientTester(Out);
        await alice.SignInAsUniqueAlice();
        var bob = appHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var chatVideoUI = bob.ScopedAppServices.GetRequiredService<ChatVideoUI>();
        chatVideoUI.StartScreenCasting(chatId);
        // Side effects on the fake JS object, so there is nothing for When to react to
        await TestWait.WhenPolled(() => {
            recorderJS.Invocations["startScreenCast"].Should().ContainSingle();
            return Task.CompletedTask;
        });

        // act
        await bob.DisposeAsync();

        // assert
        await TestWait.WhenPolled(() => {
            recorderJS.Invocations["stopRecording"].Should().ContainSingle();
            recorderJS.Invocations["dispose"].Should().ContainSingle(
                "only disposal stops the recorder's loops, and stopping fails once the circuit is gone");
            return Task.CompletedTask;
        });
    }
}
