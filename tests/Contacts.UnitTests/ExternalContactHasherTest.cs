using ActualChat.Testing.Contacts;

namespace ActualChat.Contacts.UnitTests;

public class ExternalContactHasherTest
{
    [Fact]
    public void ShouldGenerateSameHash()
    {
        // arrange
        var externalContactGenerator = new ExternalContactGenerator();
        var externalContact1 = externalContactGenerator.NewExternalContact();
        var externalContact2 = ObjectExt.Clone(externalContact1);

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
        var externalContactGenerator = new ExternalContactGenerator();
        var id = externalContactGenerator.NewId();
        var externalContact1 = new ExternalContactFull(id) {
            PhoneHashes = externalContactGenerator.NewPhoneHashes(1, 1),
        };
        var externalContact2 = new ExternalContactFull(id) {
            PhoneHashes = externalContactGenerator.NewPhoneHashes(1, 1),
        };

        // act
        var sut = new ExternalContactHasher();
        var hash1 = sut.Compute(externalContact1);
        var hash2 = sut.Compute(externalContact2);

        // assert
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ShouldComputeHashOfList()
    {
        // arrange
        var externalContactGenerator = new ExternalContactGenerator();
        var userDeviceId = externalContactGenerator.NewUserDeviceId();
        var externalContacts = Enumerable.Range(1, 10_000)
            .Select(i => externalContactGenerator.NewExternalContact(userDeviceId, i))
            .ToArray();

        // act
        var sut = new ExternalContactHasher();
        var hash = sut.Compute(externalContacts);
        var hashOfReversed = sut.Compute(externalContacts.Reverse());

        // assert
        hash.Should().Be(hashOfReversed);
    }
}
