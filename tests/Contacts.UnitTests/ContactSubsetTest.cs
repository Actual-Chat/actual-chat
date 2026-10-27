namespace ActualChat.Contacts.UnitTests;

public sealed class ContactSubsetTest
{
    [Fact]
    public void EqualSubsetsShouldBeEqual()
    {
        // arrange
        var placeId = PlaceId.New();

        // act
        var subsets = new[] { ContactSubset.All(), ContactSubset.Chats(), ContactSubset.Place(placeId) };
        var sameSubsets = new[] { ContactSubset.All(), ContactSubset.Chats(), ContactSubset.Place(placeId) };

        // assert
        subsets.Should().Equal(sameSubsets);
        subsets.Select(x => x.GetHashCode()).Should().Equal(sameSubsets.Select(x => x.GetHashCode()));
        subsets.Should().OnlyHaveUniqueItems();
        ContactSubset.Place(placeId).Should().NotBe(ContactSubset.Place(PlaceId.New()));
    }
}
