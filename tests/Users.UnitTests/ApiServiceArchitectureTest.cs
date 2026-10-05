using ActualChat.OAuth;
using ActualChat.Testing;
using ActualChat.Users.Email;
using ActualChat.Users.Passkeys;
using ActualChat.Users.Phone;

namespace ActualChat.Users.UnitTests;

public sealed class ApiServiceArchitectureTest
{
    [Theory]
    [InlineData(typeof(ITotpCodesBackend))]
    [InlineData(typeof(IPasskeysBackend))]
    [InlineData(typeof(IAppUpdatesBackend))]
    [InlineData(typeof(ISystemPropertiesBackend))]
    [InlineData(typeof(IOAuthGrantsBackend))]
    public void BackendsShouldNotReceiveSessionContext(Type serviceType)
    {
        // act
        var assertContext = () => serviceType.AssertNoSessionContext();

        // assert
        assertContext.Should().NotThrow();
    }

    [Theory]
    [InlineData(typeof(ISessionContextBackend))]
    [InlineData(typeof(ISessionHashBackend))]
    [InlineData(typeof(INestedSessionBackend))]
    public void BoundaryAssertionShouldRejectSessionsAndSessionIdentifiers(Type serviceType)
    {
        // act
        var assertContext = () => serviceType.AssertNoSessionContext();

        // assert
        assertContext.Should().Throw<Exception>();
    }

    [Theory]
    [InlineData(typeof(ITotpCodesBackend), "Generate", typeof(string))]
    [InlineData(typeof(ITotpCodesBackend), "Validate", typeof(string))]
    [InlineData(typeof(ITotpCodesBackend), "IsEmailThrottled", typeof(string))]
    [InlineData(typeof(ITotpCodesBackend), "IsPhoneThrottled", typeof(ActualChat.Phone))]
    [InlineData(typeof(ITotpCodesBackend), "GetLastChannel", typeof(ActualChat.Phone))]
    [InlineData(typeof(ITotpCodesBackend), "SetLastChannel", typeof(ActualChat.Phone))]
    [InlineData(typeof(IPasskeysBackend), "StoreChallenge", typeof(UserId))]
    [InlineData(typeof(IPasskeysBackend), "ConsumeChallenge", typeof(UserId))]
    public void AuthenticationBackendsShouldRouteByBusinessKeys(Type serviceType, string methodName, Type keyType)
    {
        // act
        var firstParameter = serviceType.GetMethod(methodName)!.GetParameters()[0];

        // assert
        firstParameter.ParameterType.Should().Be(keyType);
    }

    [Theory]
    [InlineData(typeof(ChatPositions))]
    [InlineData(typeof(Emails))]
    [InlineData(typeof(EmailAuth))]
    [InlineData(typeof(PhoneAuth))]
    [InlineData(typeof(PasskeyAuth))]
    [InlineData(typeof(SystemProperties))]
    [InlineData(typeof(OAuthGrants))]
    [InlineData(typeof(ActualChat.Users.AppUpdates))]
    public void ApiServicesShouldNotDependOnStorage(Type serviceType)
    {
        // act
        var assertDependencies = () => serviceType.AssertNoStorageDependencies();

        // assert
        assertDependencies.Should().NotThrow();
    }

    // Nested types

    private interface ISessionContextBackend
    {
        Task Store(Session context);
    }

    private interface ISessionHashBackend
    {
        Task Store(string sessionHash);
    }

    private interface INestedSessionBackend
    {
        Task Store(ContextRequest request);
    }

    private record ContextRequest(Session Context);
}
