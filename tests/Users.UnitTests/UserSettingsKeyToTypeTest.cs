using System.Reflection;

namespace ActualChat.Users.UnitTests;

/// <summary>
/// Guards the registration a settings type needs on the server: without it
/// <see cref="UserSettings"/> reads the stored value back as the abstract base and returns null,
/// so the setting silently reverts to its defaults.
/// </summary>
public sealed class UserSettingsKeyToTypeTest
{
    [Fact]
    public void EveryAccessorTypeShouldBeRegistered()
    {
        // arrange
        // A single-parameter accessor keys its settings by the type name; the rest take
        // an id and land on the parameterized "@" path, which UserSettings resolves separately.
        var accessorTypes = typeof(UserSettingsUIExt)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(x => x.GetParameters().Length == 1
                && x.ReturnType.IsGenericType
                && x.ReturnType.GetGenericTypeDefinition() == typeof(UserSettingsAccessor<>))
            .Select(x => x.ReturnType.GetGenericArguments()[0])
            .ToList();

        // act
        var unregistered = accessorTypes
            .Where(x => !UserSettings.KeyToType.ContainsKey(x.Name))
            .Select(x => x.Name)
            .ToList();

        // assert
        accessorTypes.Should().NotBeEmpty("the reflection query must keep matching UserSettingsUIExt");
        unregistered.Should().BeEmpty("UserSettings.KeyToType must list every type UserSettingsUIExt exposes");
    }
}
