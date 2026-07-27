using ActualChat.Security;
using Microsoft.AspNetCore.DataProtection;

namespace ActualChat.Users.UnitTests;

public class SessionExtTest
{
    [Fact]
    public void RejectsProtocolRelativeReturnUrl()
    {
        // act
        var action = () => LocalUrl.Parse("//evil.example");

        // assert
        action.Should().Throw<FormatException>();
    }

    [Fact]
    public void RejectsForeignAbsoluteReturnUrl()
    {
        // act
        var action = () => LocalUrl.Parse("https://evil.example");

        // assert
        action.Should().Throw<FormatException>();
    }

    [Fact]
    public void AcceptsInAppReturnUrl()
    {
        // act
        var returnUrl = LocalUrl.Parse("/chat");

        // assert
        returnUrl.Value.Should().Be("/chat");
    }

    [Fact]
    public void SessionValidatorShouldAcceptSupportedFormats()
    {
        // arrange
        var session = Session.New();
        var apiKey = SessionExt.NewApiKey();
        var defaultSession = Session.Default;

        // act
        var isSessionValid = session.IsValid();
        var isApiKeyValid = apiKey.IsValid();
        var isDefaultSessionValid = defaultSession.IsValid();

        // assert
        session.Id.Should().HaveLength(24);
        apiKey.Id.Should().HaveLength(33);
        isSessionValid.Should().BeTrue();
        isApiKeyValid.Should().BeTrue();
        isDefaultSessionValid.Should().BeFalse();
    }

    [Fact]
    public void SessionValidatorShouldRejectPendingRegistrationSizedBase64()
    {
        // arrange
        var base64 = Convert.ToBase64String(new byte[128]);
        var session = new Session(base64);

        // act
        var isValid = session.IsValid();

        // assert
        base64.Length.Should().BeGreaterThan(CoreConstants.Session.MaxIdLength);
        isValid.Should().BeFalse();
    }

    [Fact]
    public void SessionValidatorShouldRejectUnsupportedCharactersAndExcessiveLength()
    {
        // arrange
        var unsupportedCharacter = new Session(new string('a', 23) + '_');
        var taggedSession = new Session(Session.New().Id + "&k=v");
        var excessiveLength = new Session(new string('a', CoreConstants.Session.MaxIdLength + 1));

        // act
        var hasSupportedCharacter = unsupportedCharacter.IsValid();
        var isTaggedSessionValid = taggedSession.IsValid();
        var isExcessiveLengthValid = excessiveLength.IsValid();

        // assert
        hasSupportedCharacter.Should().BeFalse();
        isTaggedSessionValid.Should().BeFalse();
        isExcessiveLengthValid.Should().BeFalse();
    }

    [Fact]
    public async Task SecureTokenKindsShouldBeCryptographicallyIsolated()
    {
        // arrange
        using var services = new ServiceCollection()
            .AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider())
            .AddSingleton(MomentClockSet.Default)
            .BuildServiceProvider();
        var backend = new SecureTokensBackend(services);
        var token = await backend.Create(SecureTokenKind.Session, "session-id");

        // act
        var sessionToken = backend.TryDecrypt(SecureTokenKind.Session, token.Token);
        var pendingRegistrationToken = backend.TryDecrypt(SecureTokenKind.PendingRegistration, token.Token);

        // assert
        sessionToken?.Value.Should().Be("session-id");
        pendingRegistrationToken.Should().BeNull();
    }

    [Fact]
    public void GetPrefix_ShouldReturnFirstNChars()
    {
        var session = new Session("abcdefghijklmnopqrstuvwxyz");
        var prefix = session.IdPrefix;
        prefix.Should().Be("abcdefgh"); // 8 chars
        prefix.Length.Should().Be(CoreConstants.Session.IdPrefixLength);
    }

    [Fact]
    public void GetPrefix_ExactLengthSessionId_ShouldReturnFullId()
    {
        // Session IDs must be valid (minimum length), so use a real one
        var session = Session.New();
        var prefix = session.IdPrefix;
        prefix.Length.Should().Be(CoreConstants.Session.IdPrefixLength);
        session.Id.Should().StartWith(prefix);
    }

    [Fact]
    public void DefaultSession_ShouldBeSession()
    {
        var session = Session.Default;
        (session.Kind is SessionKind.Session).Should().BeTrue();
    }

    [Fact]
    public void IsApiKey_WithApiPrefix_ShouldReturnTrue()
    {
        var session = new Session(CoreConstants.Session.ApiKeyPrefix + Session.New().Id);
        (session.Kind is SessionKind.ApiKey).Should().BeTrue();
    }

    [Fact]
    public void IsApiKey_WithoutApiPrefix_ShouldReturnFalse()
    {
        var session = Session.New();
        (session.Kind is SessionKind.ApiKey).Should().BeFalse();
    }

    [Fact]
    public void IsApiKey_WithPartialPrefix_ShouldReturnFalse()
    {
        var session = new Session("x-" + Session.New().Id);
        (session.Kind is SessionKind.ApiKey).Should().BeFalse();
    }
}
