using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;
using Bunit;
using Moq;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class StartupLanguageTest
{
    [Theory]
    [InlineData("ru", "en", "en")]
    [InlineData(null, "en", "en")]
    [InlineData("en", null, "ru")]
    public async Task SavedLanguageShouldBeAppliedAndPersisted(
        string? cachedLanguage, string? savedLanguage, string expectedLanguage)
    {
        // arrange
        var whenReadSource = TaskCompletionSourceExt.New<StoredSettings?>();
        await using var context = NewContext(whenReadSource.Task, cachedLanguage);
        var hub = context.Services.GetRequiredService<AppUIHub>();
        var starter = new AppScopedServiceStarter(hub);

        // act
        var resolveTask = starter.ResolveUILanguage(hub.BrowserInfo);
        resolveTask.IsCompleted.Should().BeFalse();
        whenReadSource.SetResult(new UserLanguageSettings {
            UILanguage = Language.TryParse(savedLanguage, allowNull: true),
        });
        await TestWait.WhenPolled(() => resolveTask.IsCompleted.Should().BeTrue());
        await resolveTask;

        // assert
        hub.LocalizationUI.Language.Should().Be(Language.Parse(expectedLanguage));
        var call = context.JSInterop.Invocations["window.LocalizationUI.setDocumentLanguage"].Single();
        call.Arguments.Should().Equal(Language.Parse(expectedLanguage).Value, true,
            Language.TryParse(savedLanguage, allowNull: true)?.Value);
    }

    [Fact]
    public async Task StartupShouldNotWaitForJavaScriptLanguageUpdate()
    {
        // arrange
        await using var context = NewContext(null, "en", "de");
        var pendingCall = context.JSInterop.SetupVoid("window.LocalizationUI.setDocumentLanguage", _ => true);
        var hub = context.Services.GetRequiredService<AppUIHub>();
        var starter = new AppScopedServiceStarter(hub);

        // act
        var resolveTask = starter.ResolveUILanguage(hub.BrowserInfo);

        // assert
        try {
            resolveTask.IsCompletedSuccessfully.Should().BeTrue();
            hub.LocalizationUI.WhenReady.IsCompletedSuccessfully.Should().BeTrue();
            hub.LocalizationUI.Language.Should().Be(Languages.German);
            pendingCall.Invocations.Should().ContainSingle();
        }
        finally {
            pendingCall.SetVoidResult();
        }
    }

    [Fact]
    public async Task OverrideShouldApplyWithoutReadingOrPersistingSettings()
    {
        // arrange
        await using var context = NewContext(null, "en", "de");
        var hub = context.Services.GetRequiredService<AppUIHub>();
        var starter = new AppScopedServiceStarter(hub);

        // act
        await starter.ResolveUILanguage(hub.BrowserInfo);

        // assert
        hub.LocalizationUI.Language.Should().Be(Languages.German);
        var call = context.JSInterop.Invocations["window.LocalizationUI.setDocumentLanguage"].Single();
        call.Arguments.Should().Equal(Languages.German.Value, false, Languages.German.Value);
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData(null, "ru")]
    public async Task SlowSettingsShouldFallBackWithoutPersisting(string? cachedLanguage, string expectedLanguage)
    {
        // arrange
        var whenReadSource = TaskCompletionSourceExt.New<StoredSettings?>();
        await using var context = NewContext(whenReadSource.Task, cachedLanguage);
        var hub = context.Services.GetRequiredService<AppUIHub>();
        var starter = new AppScopedServiceStarter(hub);

        // act
        var resolveTask = starter.ResolveUILanguage(hub.BrowserInfo);
        await TestWait.WhenPolled(() => resolveTask.IsCompleted.Should().BeTrue(), TimeSpan.FromSeconds(3));
        await resolveTask;
        whenReadSource.SetResult(new UserLanguageSettings {
            UILanguage = Languages.German,
            DetectedUILanguage = Languages.Russian,
        });
        await TestWait.WhenPolled(() => hub.LanguageUI.WhenReady.IsCompleted.Should().BeTrue());

        // assert
        hub.LocalizationUI.Language.Should().Be(Language.Parse(expectedLanguage));
        var calls = context.JSInterop.Invocations["window.LocalizationUI.setDocumentLanguage"];
        var call = calls.Should().ContainSingle().Subject;
        var expected = Language.Parse(expectedLanguage).Value;
        call.Arguments.Should().Equal(expected, false, expected);
        context.JSInterop.Invocations.Should().NotContain(x => x.Identifier == "window.LocalizationUI.set");
    }

    [Theory]
    [InlineData("en")]
    [InlineData(null)]
    public async Task SelectingLanguageShouldPersistLocally(string? selectedLanguage)
    {
        // arrange
        var savedSettings = new UserLanguageSettings { DetectedUILanguage = Languages.German };
        await using var context = NewContext(Task.FromResult<StoredSettings?>(savedSettings), "ru", "de");
        var hub = context.Services.GetRequiredService<AppUIHub>();
        var language = Language.TryParse(selectedLanguage, allowNull: true);

        // act
        await hub.LanguageUI.UpdateUILanguage(language);

        // assert
        hub.LanguageUI.Settings.Value.UILanguage.Should().Be(language);
        hub.LanguageUI.Settings.Value.DetectedUILanguage.Should().Be(
            language is null ? Languages.Russian : Languages.German);
        var call = context.JSInterop.Invocations["window.LocalizationUI.set"].Should().ContainSingle().Subject;
        call.Arguments.Should().Equal(new object?[] { language?.Value });
    }

    [Fact]
    public async Task FirstSettingsReadShouldCaptureTheBrowserLanguage()
    {
        // arrange
        await using var context = NewContext(Task.FromResult<StoredSettings?>(null), null, "de");
        var hub = context.Services.GetRequiredService<AppUIHub>();

        // act
        await TestWait.WhenPolled(() => hub.LanguageUI.WhenReady.IsCompleted.Should().BeTrue());

        // assert
        hub.LanguageUI.Settings.Value.UILanguage.Should().BeNull();
        hub.LanguageUI.Settings.Value.DetectedUILanguage.Should().Be(Languages.Russian);
    }

    [Fact]
    public async Task ExistingSettingsWithoutDetectedLanguageShouldCaptureItOnce()
    {
        // arrange
        var savedSettings = new UserLanguageSettings { UILanguage = Languages.English };
        await using var context = NewContext(Task.FromResult<StoredSettings?>(savedSettings), "en", "de");
        var hub = context.Services.GetRequiredService<AppUIHub>();

        // act
        await TestWait.WhenPolled(() =>
            hub.LanguageUI.Settings.Value.DetectedUILanguage.Should().Be(Languages.Russian));

        // assert
        hub.LanguageUI.Settings.Value.UILanguage.Should().Be(Languages.English);
        hub.LanguageUI.Settings.Value.DetectedUILanguage.Should().Be(Languages.Russian);
    }

    [Fact]
    public async Task StartupShouldKeepTheAccountDetectedLanguageFromAnotherBrowser()
    {
        // arrange
        var savedSettings = new UserLanguageSettings { DetectedUILanguage = Languages.German };
        await using var context = NewContext(Task.FromResult<StoredSettings?>(savedSettings), null);
        var hub = context.Services.GetRequiredService<AppUIHub>();
        var starter = new AppScopedServiceStarter(hub);

        // act
        await starter.ResolveUILanguage(hub.BrowserInfo);

        // assert
        hub.LocalizationUI.Language.Should().Be(Languages.Russian);
        hub.LanguageUI.Settings.Value.DetectedUILanguage.Should().Be(Languages.German);
    }

    // Private methods

    private static BunitContext NewContext(
        Task<StoredSettings?>? whenRead, string? cachedLanguage, string? languageOverride = null)
    {
        var context = TestBunitContext.New();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddScoped<AppUIHub>();
        context.Services.AddScoped<LocalizationUI>();
        context.Services.AddScoped(c => new UserSettingsUI(c, c.Session(), c.GetService<IChatUserSettingsSource>()));
        context.Services.AddScoped<BrowserInfo>(c => new TestBrowserInfo(c.GetRequiredService<UIHub>(),
            new(languageOverride, cachedLanguage, ["ru-RU"])));
        if (whenRead is not null) {
            var userSettings = new Mock<IUserSettings>(MockBehavior.Strict);
            userSettings.Setup(x => x.Get(It.IsAny<Session>(), UserLanguageSettings.KvasKey,
                    It.IsAny<CancellationToken>()))
                .Returns(whenRead);
            context.Services.AddSingleton(userSettings.Object);
            context.Services.AddScoped<LanguageUI>();
        }
        context.Services.GetRequiredService<ISessionResolver>().Session = Session.New();
        return context;
    }

    // Nested types

    private sealed class TestBrowserInfo : BrowserInfo
    {
        public TestBrowserInfo(UIHub hub, IBrowserInfoBackend.UILanguageInfo languageInfo) : base(hub)
        {
            UpdateUILanguageInfo(languageInfo);
            WhenReadySource.TrySetResult();
        }
    }
}
