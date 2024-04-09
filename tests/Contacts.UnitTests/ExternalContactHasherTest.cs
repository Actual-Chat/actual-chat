using ActualLab.Generators;

namespace ActualChat.Contacts.UnitTests;

public class ExternalContactHasherTest
{
    [Fact]
    public void ShouldGenerateSameHash()
    {
        // arrange
        var userDeviceId = NewUserDeviceId();
        var deviceContactId = new Symbol(RandomStringGenerator.Default.Next());
        var id = new ExternalContactId(userDeviceId, deviceContactId);
        var externalContact1 = new ExternalContactFull(id) {
            PhoneHashes = new ApiSet<string>(new[] { "123" }),
        };
        var externalContact2 = new ExternalContactFull(id) {
            PhoneHashes = new ApiSet<string>(new[] { "123" }),
        };

        // act
        var sut = new ExternalContactHasher();
        var hash1 = sut.Compute(externalContact1);
        var hash2 = sut.Compute(externalContact2);

        // assert
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ShouldDetectChangesInPhone()
    {
        // arrange
        var userDeviceId = NewUserDeviceId();
        var deviceContactId = new Symbol(RandomStringGenerator.Default.Next());
        var id = new ExternalContactId(userDeviceId, deviceContactId);
        var externalContact1 = new ExternalContactFull(id) {
            PhoneHashes = new ApiSet<string>(new[] { "123" }),
        };
        var externalContact2 = new ExternalContactFull(id) {
            PhoneHashes = new ApiSet<string>(new[] { "456" }),
        };

        // act
        var sut = new ExternalContactHasher();
        var hash1 = sut.Compute(externalContact1);
        var hash2 = sut.Compute(externalContact2);

        // assert
        hash1.Should().NotBe(hash2);
    }

    private static UserDeviceId NewUserDeviceId()
    {
        var deviceId = new Symbol(RandomStringGenerator.Default.Next());
        var userDeviceId = new UserDeviceId(UserId.New(), deviceId);
        return userDeviceId;
    }
}
