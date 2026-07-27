using System.Data.Common;
using System.Net.Sockets;
using ActualLab.Resilience;

namespace ActualChat.Core.UnitTests;

public class ComputedTransiencyResolverTest
{
    [Theory]
    [InlineData(typeof(NotFoundException<Account>))]
    [InlineData(typeof(InternalError))]
    [InlineData(typeof(WrongShardException))]
    [InlineData(typeof(PostponeException))]
    public void UnrecognizedErrorsAreNonTransient(Type errorType)
    {
        var error = (Exception)Activator.CreateInstance(errorType)!;
        ComputedTransiencyResolver.Instance.Invoke(error).Should().Be(Transiency.NonTransient);
        // The default Fusion policy is what we're overriding - it would spin these at 1s
        TransiencyResolvers.PreferTransient.Invoke(error).Should().Be(Transiency.Transient);
    }

    [Fact]
    public void InfrastructureErrorsStayTransient()
    {
        ComputedTransiencyResolver.Instance.Invoke(new TestDbException()).Should().Be(Transiency.Transient);
        ComputedTransiencyResolver.Instance.Invoke(new SocketException(10054)).Should().Be(Transiency.Transient);
        ComputedTransiencyResolver.Instance.Invoke(new HttpRequestException("x")).Should().Be(Transiency.Transient);
        ComputedTransiencyResolver.Instance.Invoke(new TimeoutException()).Should().Be(Transiency.Transient);
    }

    [Fact]
    public void MarkerInterfacesStillWin()
    {
        ComputedTransiencyResolver.Instance.Invoke(new ExternalError("x")).Should().Be(Transiency.Transient);
        ComputedTransiencyResolver.Instance.Invoke(new TransientException("x")).Should().Be(Transiency.Transient);
        ComputedTransiencyResolver.Instance.Invoke(new RetryRequiredException("x")).Should().Be(Transiency.SuperTransient);
        ComputedTransiencyResolver.Instance.Invoke(new TerminalException("x")).Should().Be(Transiency.Terminal);
    }

    [Fact]
    public void ArgumentErrorsRemainNonTransient()
    {
        // SessionsBackend.Get throws this for an invalid session
        ComputedTransiencyResolver.Instance.Invoke(new ArgumentOutOfRangeException("session"))
            .Should().Be(Transiency.NonTransient);
    }

    private sealed class TestDbException : DbException
    {
        public TestDbException() : base("test") { }
    }
}
