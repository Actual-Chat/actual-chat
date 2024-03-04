using ActualChat.Testing.Host;
using ActualLab.Versioning;

namespace ActualChat.Search.IntegrationTests;

[Collection(nameof(SearchCollection))]
public class ContactIndexStateTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private IContactIndexStatesBackend ContactIndexStatesBackend { get; } = fixture.AppHost.Services.GetRequiredService<IContactIndexStatesBackend>();
    private VersionGenerator<long> VersionGenerator { get; } = fixture.AppHost.Services.GetRequiredService<VersionGenerator<long>>();

    [Fact]
    public async Task ShouldGetAndChange()
    {
        await Run(() => ContactIndexStatesBackend.GetForChats(CancellationToken.None), () => new ChatId(Generate.Option));
        await Run(() => ContactIndexStatesBackend.GetForUsers(CancellationToken.None), () => UserId.New());

        async Task Run(Func<Task<ContactIndexState>> get, Func<Symbol> generateLastUpdatedId)
        {
            // act
            var initialState = await get();

            // assert
            initialState.IsStored().Should().BeFalse();

            // act
            var stateToCreate = initialState with {
                LastUpdatedId = generateLastUpdatedId(),
                LastUpdatedVersion = VersionGenerator.NextVersion(),
            };
            var createCmd = new ContactIndexStatesBackend_Change(initialState.Id, null, Change.Create(stateToCreate));
            var createdState = await Commander.Call(createCmd);

            // assert
            createdState.IsStored().Should().BeTrue();
            createdState.Should().BeEquivalentTo(stateToCreate, o => o.Excluding(x => x.Version));
            createdState.Version.Should().BeGreaterThan(initialState.Version);

            // act
            var stateToUpdate = createdState with {
                LastUpdatedId = generateLastUpdatedId(),
                LastUpdatedVersion = VersionGenerator.NextVersion(),
            };
            var updateCmd = new ContactIndexStatesBackend_Change(initialState.Id, createdState.Version, Change.Update(stateToUpdate));
            var updatedState = await Commander.Call(updateCmd);

            // assert
            updatedState.Should().BeEquivalentTo(stateToUpdate, o => o.Excluding(x => x.Version));
            updatedState.Version.Should().BeGreaterThan(stateToUpdate.Version);
        }
    }
}
