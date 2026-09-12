using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.IntegrationTests;

public class PasskeyJsonTest
{
    [Fact]
    public void ParseCreationOptionsShouldExtractRpUserAndChallenge()
    {
        // arrange
        const string json = """
            {"rp":{"id":"voxt.ai","name":"Voxt"},"user":{"id":"AQID","name":"alice","displayName":"Alice"},
             "challenge":"BAUG","pubKeyCredParams":[{"type":"public-key","alg":-7}]}
            """;

        // act
        var options = PasskeyJson.ParseCreationOptions(json);

        // assert
        options.RpId.Should().Be("voxt.ai");
        options.UserName.Should().Be("alice");
        options.UserId.Should().Equal([1, 2, 3], "user.id is base64url");
        options.Challenge.Should().Equal([4, 5, 6], "challenge is base64url");
    }

    [Fact]
    public void ParseRequestOptionsShouldExtractRpAndChallenge()
    {
        // arrange
        const string json = """{"challenge":"BAUG","rpId":"voxt.ai","allowCredentials":[]}""";

        // act
        var options = PasskeyJson.ParseRequestOptions(json);

        // assert
        options.RpId.Should().Be("voxt.ai");
        options.Challenge.Should().Equal([4, 5, 6], "challenge is base64url");
    }

    [Fact]
    public void AttestationJsonShouldMatchWebAuthnShape()
    {
        // act
        var json = PasskeyJson.Attestation(
            credentialId: [1, 2, 3], clientDataJson: [4], attestationObject: [5]);

        // assert
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("id").GetString().Should().Be("AQID");
        root.GetProperty("rawId").GetString().Should().Be("AQID");
        root.GetProperty("type").GetString().Should().Be("public-key");
        root.GetProperty("response").GetProperty("clientDataJSON").GetString().Should().Be("BA");
        root.GetProperty("response").GetProperty("attestationObject").GetString().Should().Be("BQ");
        root.GetProperty("response").GetProperty("transports")[0].GetString().Should().Be("internal");
    }

    [Fact]
    public void AssertionJsonShouldMatchWebAuthnShape()
    {
        // act
        var json = PasskeyJson.Assertion(
            credentialId: [1, 2, 3], clientDataJson: [4], authenticatorData: [5], signature: [6], userHandle: [7]);

        // assert
        using var doc = JsonDocument.Parse(json);
        var response = doc.RootElement.GetProperty("response");
        response.GetProperty("authenticatorData").GetString().Should().Be("BQ");
        response.GetProperty("signature").GetString().Should().Be("Bg");
        response.GetProperty("userHandle").GetString().Should().Be("Bw");
    }

    [Fact]
    public void AssertionJsonShouldCarryNullUserHandle()
    {
        // act
        var json = PasskeyJson.Assertion(
            credentialId: [1], clientDataJson: [2], authenticatorData: [3], signature: [4], userHandle: null);

        // assert
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("response").GetProperty("userHandle").ValueKind
            .Should().Be(JsonValueKind.Null, "a missing user handle must stay null, not become an empty string");
    }
}
