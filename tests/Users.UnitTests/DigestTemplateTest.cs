using ActualChat.Users.Templates;
using Mjml.Net;

namespace ActualChat.Users.UnitTests;

public class DigestTemplateTest
{
    [Fact]
    public async Task ShouldEndWithUnsubscribeLink()
    {
        // arrange
        var parameters = new DigestParameters {
            OtherUnreadCount = 0,
            OtherUnreadLink = "https://voxt.ai/",
            UnsubscribeLink = "https://voxt.ai/emails/digest/token/unsubscribe",
            UnreadChats = [
                new DigestParameters.DigestChat {
                    Name = "Kate",
                    Link = "https://voxt.ai/chat/x",
                    UnreadCount = 2,
                    BulletPoints = ["Hello there"],
                },
            ],
        };

        // act
        await using var renderer = new BlazorRenderer();
        var mjml = await renderer.RenderComponent<Digest>(new Dictionary<string, object?> {
            { nameof(Digest.Parameters), parameters },
        });
        var html = (await new MjmlRenderer().RenderAsync(mjml, new MjmlOptions { Beautify = false })).Html;

        // assert
        var linkIndex = html.IndexOf("https://voxt.ai/emails/digest/token/unsubscribe");
        linkIndex.Should().BePositive();
        html.Should().Contain("Turn off digest emails");
        linkIndex.Should().BeGreaterThan(html.IndexOf("All rights reserved"), "the unsubscribe line is the last one");
        linkIndex.Should().BeGreaterThan(html.IndexOf("github.png"), "the unsubscribe line is below the GitHub icon");
    }
}
