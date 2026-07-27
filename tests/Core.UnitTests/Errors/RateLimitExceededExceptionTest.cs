using ActualChat.Module;
using ActualLab.Resilience;

namespace ActualChat.Core.UnitTests.Errors;

public class RateLimitExceededExceptionTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void RetryDelayRoundTripsThroughTheMessage()
    {
        // arrange
        var error = new RateLimitExceededException(TimeSpan.FromSeconds(4.2));

        // act
        var restored = new ExceptionInfo(error).ToException();

        // assert
        error.Message.Should().Be("Too many requests. Retry in 5s.");
        restored.Should().BeOfType<RateLimitExceededException>()
            .Which.RetryDelay.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void SubSecondRetryDelayRoundTripsAsOneSecond()
    {
        // arrange
        var error = new RateLimitExceededException(TimeSpan.FromMilliseconds(120));

        // act
        var restored = new ExceptionInfo(error).ToException();

        // assert
        error.Message.Should().Be("Too many requests. Retry in 1s.");
        restored.Should().BeOfType<RateLimitExceededException>()
            .Which.RetryDelay.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void MessageWithoutRetryDelayYieldsZeroDelay()
    {
        // act
        var noMessage = new RateLimitExceededException();
        var otherMessage = new RateLimitExceededException("Too many requests. Retry in a while.");
        var garbledMessage = new RateLimitExceededException("Too many requests. Retry in 999999999999999999999s.");

        // assert
        noMessage.RetryDelay.Should().Be(TimeSpan.Zero);
        noMessage.Message.Should().Be("Too many requests.");
        otherMessage.RetryDelay.Should().Be(TimeSpan.Zero);
        garbledMessage.RetryDelay.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void RateLimitErrorIsTransient1()
    {
        // arrange
        ApiContractsModuleInitializer.Load();

        // act
        var transiency = TransiencyResolvers.PreferTransient
            .Invoke(new RateLimitExceededException(TimeSpan.FromSeconds(5)));

        // assert
        transiency.Should().Be(Transiency.Transient);
    }

    [Fact]
    public void RateLimitErrorIsTransient2()
    {
        // arrange
        ApiContractsModuleInitializer.Load();

        // act
        var transiency = ComputedTransiencyResolver.Instance
            .Invoke(new RateLimitExceededException(TimeSpan.FromSeconds(5)));

        // assert
        transiency.Should().Be(Transiency.Transient);
    }
}
