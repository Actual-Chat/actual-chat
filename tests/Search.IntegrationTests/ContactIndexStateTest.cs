using ActualChat.Testing.Host;
using ActualChat.Testing.Host.Assertion;
using ActualLab.Versioning;

namespace ActualChat.Search.IntegrationTests;

[Collection(nameof(SearchCollection))]
public class ContactIndexStateTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private IContactIndexStatesBackend ContactIndexStatesBackend { get; } = fixture.AppHost.Services.GetRequiredService<IContactIndexStatesBackend>();
    private VersionGenerator<long> VersionGenerator { get; } = fixture.AppHost.Services.GetRequiredService<VersionGenerator<long>>();

    [Theory]
    [InlineData(typeof(ChatId))]
    [InlineData(typeof(UserId))]
    [InlineData(typeof(AuthorId))]
    [InlineData(typeof(PlaceId))]
    public async Task ShouldGetAndChange(Type idType)
    {
        // act
        var initialState = await Get();

        // assert
        initialState.IsStored().Should().BeFalse();

        // act
        var stateToCreate = initialState with {
            LastUpdatedId = GenerateLastUpdatedId(),
            LastUpdatedVersion = VersionGenerator.NextVersion(),
        };
        var createCmd = new ContactIndexStatesBackend_Change(initialState.Id, null, Change.Create(stateToCreate));
        var createdState = await Commander.Call(createCmd);
        var retrievedCreatedState = await Get();

        // assert
        createdState.IsStored().Should().BeTrue();
        createdState.Version.Should().BeGreaterThan(initialState.Version);
        retrievedCreatedState.Should().BeEquivalentTo(createdState, o => o.ExcludingSystemProperties());

        // act
        var stateToUpdate = createdState with {
            LastUpdatedId = GenerateLastUpdatedId(),
            LastUpdatedVersion = VersionGenerator.NextVersion(),
        };
        var updateCmd = new ContactIndexStatesBackend_Change(initialState.Id, createdState.Version, Change.Update(stateToUpdate));
        var updatedState = await Commander.Call(updateCmd);
        var retrievedUpdatedState = await Get();

        // assert
        updatedState.Should().BeEquivalentTo(stateToUpdate, o => o.ExcludingSystemProperties());
        updatedState.Version.Should().BeGreaterThan(stateToUpdate.Version);
        retrievedUpdatedState.Should().BeEquivalentTo(updatedState);
        return;

        Task<ContactIndexState> Get()
        {
            if (idType == typeof(ChatId))
                return ContactIndexStatesBackend.GetForChats(CancellationToken.None);

            if (idType == typeof(PlaceId))
                return ContactIndexStatesBackend.GetForPlaces(CancellationToken.None);

            if (idType == typeof(UserId))
                return ContactIndexStatesBackend.GetForUsers(CancellationToken.None);

            if (idType == typeof(AuthorId))
                return ContactIndexStatesBackend.GetForPlaceAuthors(CancellationToken.None);

            throw new ArgumentOutOfRangeException(nameof(idType), idType, null);
        }

        Symbol GenerateLastUpdatedId()
        {
            if (idType == typeof(ChatId))
                return new ChatId(Generate.Option);

            if (idType == typeof(PlaceId))
                return new PlaceId(Generate.Option);

            if (idType == typeof(UserId))
                return UserId.New();

            if (idType == typeof(AuthorId))
                return new AuthorId(new ChatId(Generate.Option), 1, AssumeValid.Option);

            throw new ArgumentOutOfRangeException(nameof(idType), idType, null);
        }
    }
}
