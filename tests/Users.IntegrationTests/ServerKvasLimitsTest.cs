using ActualChat.Kvas;
using ActualChat.Testing.Host;

namespace ActualChat.Users.IntegrationTests;

[Collection(nameof(UserCollection))]
public class ServerKvasLimitsTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task AcceptsSettingsSizedItem()
    {
        // arrange
        var tester = AppHost.NewWebClientTester(Out);
        await using var _ = tester.ConfigureAwait(false);
        var kvas = AppHost.Services.GetRequiredService<IServerKvas>();
        var key = $"test-{Ulid.NewUlid().ToString().ToLower()}";
        var value = new byte[1024];

        // act
        await tester.Commander.Call(new ServerKvas_Set { Session = tester.Session, Key = key, Value = value });
        var stored = await kvas.Get(tester.Session, key, CancellationToken.None);

        // assert
        stored.Should().Equal(value);
    }

    [Fact]
    public async Task RefusesOversizedValue()
    {
        // arrange
        var tester = AppHost.NewWebClientTester(Out);
        await using var _ = tester.ConfigureAwait(false);
        var key = $"test-{Ulid.NewUlid().ToString().ToLower()}";
        var value = new byte[ServerKvas_Set.MaxValueLength + 1];

        // act
        var act = () => tester.Commander.Call(new ServerKvas_Set {
            Session = tester.Session,
            Key = key,
            Value = value,
        });

        // assert
        await act.Should().ThrowAsync<Exception>().WithMessage("*too big*");
    }

    [Fact]
    public async Task RefusesOversizedKey()
    {
        // arrange
        var tester = AppHost.NewWebClientTester(Out);
        await using var _ = tester.ConfigureAwait(false);
        var key = new string('k', ServerKvas_Set.MaxKeyLength + 1);

        // act
        var act = () => tester.Commander.Call(new ServerKvas_Set {
            Session = tester.Session,
            Key = key,
            Value = [1, 2, 3],
        });

        // assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task RefusesOversizedBatch()
    {
        // arrange
        var tester = AppHost.NewWebClientTester(Out);
        await using var _ = tester.ConfigureAwait(false);
        var items = Enumerable
            .Range(0, ServerKvas_SetMany.MaxItemCount + 1)
            .Select(i => ($"test-{i.Format()}", (byte[]?)[1, 2, 3]))
            .ToArray();

        // act
        var act = () => tester.Commander.Call(new ServerKvas_SetMany { Session = tester.Session, Items = items });

        // assert
        await act.Should().ThrowAsync<Exception>().WithMessage("*cannot contain more than*");
    }
}
