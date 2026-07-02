namespace ActualChat.Users.UnitTests;

public class UserModelSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void Avatar_Basic()
    {
        var avatar = new Avatar("avatar-1", 1) {
            Name = "Test Avatar",
            Bio = "A bio",
            AvatarKey = "key-1",
            PictureUrl = "https://example.com/pic.jpg",
        };

        var s = avatar.PassThroughAllSerializers(Out);
        s.Id.Should().Be(avatar.Id);
        s.Version.Should().Be(avatar.Version);
        s.Name.Should().Be(avatar.Name);
        s.Bio.Should().Be(avatar.Bio);
        s.AvatarKey.Should().Be(avatar.AvatarKey);
        s.PictureUrl.Should().Be(avatar.PictureUrl);
    }

    [Fact]
    public void AvatarFull_Basic()
    {
        var userId = UserId.New();
        var avatar = new AvatarFull(userId, "avatar-1", 1) {
            Name = "Test Avatar",
            IsAnonymous = true,
        };

        var s = avatar.PassThroughAllSerializers(Out);
        s.UserId.Should().Be(avatar.UserId);
        s.IsAnonymous.Should().Be(avatar.IsAnonymous);
        s.Name.Should().Be(avatar.Name);
    }

    [Fact]
    public void ChatPosition_Basic()
    {
        var pos = new ChatPosition(42, "origin");
        pos.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void GuestIdOption_Basic()
    {
        var guestId = UserId.NewGuest();
        var option = new GuestIdOption(guestId);
        option.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void UserAppSettings_Basic()
    {
        var settings = new UserAppSettings {
            Origin = "https://actual.chat",
            IsDataCollectionEnabled = true,
            AreExperimentalFeaturesEnabled = false,
            IsIncompleteUIEnabled = null,
        };
        settings.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void UserAvatarSettings_Basic()
    {
        var settings = new UserAvatarSettings {
            AvatarIds = ["avatar-1", "avatar-2"],
            DefaultAvatarId = "avatar-1",
        };
        settings.AssertPassesThroughAllSerializers(v => {
            v.AvatarIds.Should().Equal(settings.AvatarIds);
            v.DefaultAvatarId.Should().Be(settings.DefaultAvatarId);
        }, Out);
    }

    [Fact]
    public void UserBubbleSettings_Basic()
    {
        var settings = new UserBubbleSettings {
            ReadBubbles = ["bubble-1", "bubble-2"],
            Origin = "https://actual.chat",
        };
        settings.AssertPassesThroughAllSerializers(v => {
            v.ReadBubbles.Should().Equal(settings.ReadBubbles);
            v.Origin.Should().Be(settings.Origin);
        }, Out);
    }

    [Fact]
    public void ChatUserSettings_Basic()
    {
        var settings = new Chat.ChatUserSettings {
            Language = Languages.English,
            NotificationMode = ChatNotificationMode.ImportantOnly,
            VoiceMode = VoiceMode.TextAndVoice,
            ListeningMode = ListeningMode.Default,
        };
        settings.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void UserChatRecordingDetectedLanguage_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var settings = new UserChatRecordingDetectedLanguage {
            Timestamp = new Moment(DateTime.UtcNow),
            ChatId = chatId,
            Language = Languages.English,
        };
        settings.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void UserEmailsSettings_Basic()
    {
        var settings = new UserEmailsSettings {
            Origin = "https://actual.chat",
            DigestTime = new TimeSpan(9, 0, 0),
            IsDigestEnabled = true,
        };
        settings.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void UserLanguageSettings_Basic()
    {
        var settings = new UserLanguageSettings {
            Primary = Languages.English,
            Secondary = Languages.Russian,
            Tertiary = Languages.German,
            Origin = "https://actual.chat",
        };
        settings.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void UserListeningSettings_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var settings = new UserListeningSettings {
            AlwaysListenedChatIds = [chatId],
            Origin = "https://actual.chat",
        };
        var s = settings.PassThroughAllSerializers(Out);
        s.AlwaysListenedChatIds.Length.Should().Be(1);
        s.AlwaysListenedChatIds[0].Should().Be(chatId);
        s.Origin.Should().Be(settings.Origin);
    }

    [Fact]
    public void UserNavbarSettings_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var placeId = PlaceId.New();
        var settings = new UserNavbarSettings {
            Origin = "https://actual.chat",
            PinnedChats = [chatId],
            PlacesOrder = [placeId],
        };
        var s = settings.PassThroughAllSerializers(Out);
        s.Origin.Should().Be(settings.Origin);
        s.PinnedChats.Length.Should().Be(1);
        s.PinnedChats[0].Should().Be(chatId);
        s.PlacesOrder.Length.Should().Be(1);
    }

    [Fact]
    public void UserOnboardingSettings_Basic()
    {
        var settings = new UserOnboardingSettings {
            IsAvatarStepCompleted = true,
            IsCreateChatsStepCompleted = true,
            IsVerifyPhoneStepCompleted = false,
            IsVerifyEmailStepCompleted = false,
            Origin = "https://actual.chat",
        };
        settings.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void UserTranscriptionEngineSettings_Basic()
    {
        var settings = new UserTranscriptionEngineSettings {
            Origin = "https://actual.chat",
        };
        settings.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void LocalAppSettings_Basic()
    {
        var settings = new LocalAppSettings {
            IsLogViewerEnabled = true,
            IsVideoDiagnosticsEnabled = true,
            LocationAccuracy = GeoTrackingAccuracy.Low,
        };
        settings.AssertPassesThroughAllSerializers(AssertLocalAppSettingsEqual);
    }

    [Fact]
    public void LocalAppSettings_Default()
    {
        var settings = new LocalAppSettings();
        settings.AssertPassesThroughAllSerializers(AssertLocalAppSettingsEqual);
    }

    [Fact]
    public void LocalAppSettings_LocationAccuracyCanBeChanged()
    {
        var settings = new LocalAppSettings();
        settings.LocationAccuracy.Should().BeNull();
        settings.LocationAccuracyOrDefault.Should().Be(GeoTrackingAccuracy.Balanced);

        var updated = settings with { LocationAccuracy = GeoTrackingAccuracy.High };
        updated.LocationAccuracy.Should().Be(GeoTrackingAccuracy.High);
        updated.LocationAccuracyOrDefault.Should().Be(GeoTrackingAccuracy.High);
    }

    [Fact]
    public void LocalAppSettings_WithCameraMirrorOverrides()
    {
        var settings = new LocalAppSettings {
            CameraMirrorOverrides = ApiMap<string, bool>.Empty
                .With("cam-1", true)
                .With("cam-2", false),
        };
        settings.AssertPassesThroughAllSerializers(AssertLocalAppSettingsEqual);
    }

    private static void AssertLocalAppSettingsEqual(LocalAppSettings actual, LocalAppSettings expected)
    {
        actual.IsLogViewerEnabled.Should().Be(expected.IsLogViewerEnabled);
        actual.SelectedCameraDeviceId.Should().Be(expected.SelectedCameraDeviceId);
        actual.IsBackgroundBlurEnabled.Should().Be(expected.IsBackgroundBlurEnabled);
        actual.IsVideoDiagnosticsEnabled.Should().Be(expected.IsVideoDiagnosticsEnabled);
        actual.LocationAccuracy.Should().Be(expected.LocationAccuracy);
        actual.CameraMirrorOverrides.Should().BeEquivalentTo(expected.CameraMirrorOverrides);
    }
}
