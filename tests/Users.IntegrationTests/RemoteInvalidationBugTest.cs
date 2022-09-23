using ActualChat.Testing.Host;
using Stl.Fusion.Bridge;
using Stl.Fusion.Bridge.Interception;
using Stl.Fusion.Interception;

namespace ActualChat.Users.IntegrationTests;

public class RemoteInvalidationBugTest : AppHostTestBase
{
    public RemoteInvalidationBugTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public async Task BasicTest()
    {
        using var appHost = await NewAppHost();
        await using var t = appHost.NewWebClientTester();

        var c1a = await Computed.Capture(() => t.Auth.GetUser(t.Session));
        var u1a = c1a.Value;
        u1a.Should().BeNull();

        // var altAuth = (DbAuthService<UsersDbContext, DbSessionInfo, DbUser, string>) t.Auth;
        // var c1aa = await Computed.Capture(() => altAuth.GetUser(t.Session));
        // c1aa.Should().BeSameAs(c1a);

        var c1b = await Computed.Capture(() => t.ClientAuth.GetUser(t.Session));
        var u1b = c1b.Value;
        u1b.Should().BeNull();

        var r1 = ((IReplicaMethodComputed)c1b).Replica!;
        var publisher = t.AppServices.GetRequiredService<IPublisher>();
        var p1 = publisher.Get(r1.PublicationRef.PublicationId)!;
        var c1bb = (Computed<User>) p1.State.UntypedComputed;
        var i1a = (ComputeMethodInput) c1a.Input;
        var i1bb =  (ComputeMethodInput) c1bb.Input;
        i1a.Function.Should().BeSameAs(i1bb.Function);
        i1a.GetType().Should().BeSameAs(i1bb.GetType());
        i1a.Arguments[0].Should().Be(i1bb.Arguments[0]);
        i1a.Target.Should().BeSameAs(i1bb.Target);
        var i1aCopy = new ComputeMethodInput(i1a.Function, i1a.MethodDef, i1a.Invocation);
        var i1bbCopy = new ComputeMethodInput(i1bb.Function, i1bb.MethodDef, i1bb.Invocation);
        i1aCopy.Should().Be(i1bbCopy);
        i1a.Should().Be(i1bb);
        c1bb.Should().BeSameAs(c1a);

        await t.SignIn(new User("Bob"));
        await Task.Delay(100);

        var c2a = await Computed.Capture(() => t.Auth.GetUser(t.Session));
        var u2a = c2a.Value;
        u2a.Should().NotBeNull();
        var c2b = await Computed.Capture(() => t.ClientAuth.GetUser(t.Session));
        var u2b = c2b.Value;
        u2b.Should().NotBeNull();
    }
}
