using ActualChat.Rpc;

namespace ActualChat.Api.Contracts.UnitTests.Rpc;

public class RpcRelayAttestationTest(ITestOutputHelper @out) : TestBase(@out)
{
    private const string Key = "hdIJ5Ae3KfGwern6V1NUEGh+iTOu6a9qb2yWpt8PMLw=";
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LineFromTheRelayScriptShouldVerify()
    {
        // arrange: signed by the relay's own script, not by RpcRelayAttestation.Sign
        const string line = "v1|kz1|172.17.0.1|1791875347|7AG09jf6P9DkeFNagu08vayEeqOkrmfCb6umvrbva10";
        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(1791875347);

        // act
        var isValid = RpcRelayAttestation.TryVerify(line, GetKeys(Key), issuedAt.AddSeconds(5), out var result);

        // assert
        isValid.Should().BeTrue();
        result!.RelayId.Should().Be("kz1");
        result.ClientIP.ToString().Should().Be("172.17.0.1");
    }

    [Fact]
    public void SignedLineShouldRoundTripForIPv6()
    {
        // arrange
        var line = RpcRelayAttestation.Sign("tr1", "2001:db8::1", Now, Key);

        // act
        var isValid = RpcRelayAttestation.TryVerify(line, GetKeys(Key), Now.AddSeconds(1), out var result);

        // assert
        isValid.Should().BeTrue();
        result!.ClientIP.ToString().Should().Be("2001:db8::1");
    }

    [Fact]
    public void LineSignedWithAnotherKeyShouldNotVerify()
    {
        // arrange
        var line = RpcRelayAttestation.Sign("kz1", "1.2.3.4", Now, "another key");

        // act & assert
        RpcRelayAttestation.TryVerify(line, GetKeys(Key), Now, out _).Should().BeFalse();
    }

    [Fact]
    public void LineWithChangedIPShouldNotVerify()
    {
        // arrange
        var line = RpcRelayAttestation.Sign("kz1", "1.2.3.4", Now, Key).Replace("1.2.3.4", "5.6.7.8");

        // act & assert
        RpcRelayAttestation.TryVerify(line, GetKeys(Key), Now, out _).Should().BeFalse();
    }

    [Fact]
    public void LineOfAnotherRelayShouldNotVerifyWithThisRelaysKey()
    {
        // arrange
        var line = RpcRelayAttestation.Sign("tr1", "1.2.3.4", Now, Key);

        // act & assert
        RpcRelayAttestation.TryVerify(line, x => x == "kz1" ? [Key] : [], Now, out _).Should().BeFalse();
    }

    [Fact]
    public void OldKeyShouldStillVerifyDuringRotation()
    {
        // arrange
        var line = RpcRelayAttestation.Sign("kz1", "1.2.3.4", Now, "old key");

        // act & assert
        RpcRelayAttestation.TryVerify(line, GetKeys("new key", "old key"), Now, out _).Should().BeTrue();
    }

    [Theory]
    [InlineData(59, true)]
    [InlineData(61, false)]
    [InlineData(-5, true)]
    [InlineData(-30, false)]
    public void AgeShouldBeLimited(int ageSeconds, bool expected)
    {
        // arrange
        var line = RpcRelayAttestation.Sign("kz1", "1.2.3.4", Now.AddSeconds(-ageSeconds), Key);

        // act & assert
        RpcRelayAttestation.TryVerify(line, GetKeys(Key), Now, out _).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("v2|kz1|1.2.3.4|1791875347|AAAA")]
    [InlineData("v1|kz1|not-an-ip|1791875347|AAAA")]
    [InlineData("v1|kz1|1.2.3.4|soon|AAAA")]
    [InlineData("v1||1.2.3.4|1791875347|AAAA")]
    [InlineData("v1|kz1|1.2.3.4|1791875347|***")]
    [InlineData("v1|kz1|1.2.3.4|1791875347|AAAA|extra")]
    public void MalformedLineShouldNotVerify(string? line)
    {
        // act & assert
        RpcRelayAttestation.IsWellFormed(line).Should().BeFalse();
        RpcRelayAttestation.TryVerify(line, GetKeys(Key), Now, out _).Should().BeFalse();
    }

    [Fact]
    public void WellFormedCheckShouldNotNeedTheKey()
    {
        // arrange
        var line = RpcRelayAttestation.Sign("kz1", "1.2.3.4", Now, "any key");

        // act & assert
        RpcRelayAttestation.IsWellFormed(line).Should().BeTrue();
    }

    // Private methods

    private static Func<string, IReadOnlyList<string>> GetKeys(params string[] keys)
        => _ => keys;
}
