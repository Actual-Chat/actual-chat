using ActualChat.Diff;
using ActualLab.Generators;

namespace ActualChat.Contacts.UnitTests;

public class ContactDiffTest
{
    [Fact]
    public void ShouldNotDetectChanges()
    {
        var deviceId = new Symbol(RandomStringGenerator.Default.Next());
        var deviceContactId = new Symbol(RandomStringGenerator.Default.Next());
        var id = new ExternalContactId(UserId.New(), deviceId, deviceContactId);
        var original = new ExternalContact(id) {
            PhoneHashes = new ApiSet<string>(new[]{"123"}),
        };
        var updated = new ExternalContact(id) {
            PhoneHashes = new ApiSet<string>(new[] { "123" }),
        };
        var diff = DiffEngine.Default.Diff<ExternalContact, ExternalContactDiff>(original, updated);
        diff.Should().Be(ExternalContactDiff.Empty);
    }

    [Fact]
    public void ShouldDetectChangesInPhone()
    {
        var deviceId = new Symbol(RandomStringGenerator.Default.Next());
        var deviceContactId = new Symbol(RandomStringGenerator.Default.Next());
        var id = new ExternalContactId(UserId.New(), deviceId, deviceContactId);
        var original = new ExternalContact(id) {
            PhoneHashes = new ApiSet<string>(new[]{"123"}),
        };
        var updated = new ExternalContact(id) {
            PhoneHashes = new ApiSet<string>(new[] { "456" }),
        };
        var diff = DiffEngine.Default.Diff<ExternalContact, ExternalContactDiff>(original, updated);
        var patched = DiffEngine.Default.Patch(original, diff);
        patched.PhoneHashes.Should().BeEquivalentTo(new[] { "456" });
    }
}
