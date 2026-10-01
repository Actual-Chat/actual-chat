using System.Net;
using ActualChat.Localization;
using ActualChat.Users.Templates;
using Microsoft.Extensions.Localization;
using Mjml.Net;
using IComponent = Microsoft.AspNetCore.Components.IComponent;

namespace ActualChat.Users.UnitTests;

public class EmailTemplateLocalizationTest
{
    private const string ChatName = "Kate";

    public static TheoryData<string> TranslatedLanguages
        => new(Languages.AllUI.Where(x => x != Languages.English).Select(x => x.Value));

    [Theory]
    [MemberData(nameof(TranslatedLanguages))]
    public async Task VerificationEmailShouldUseRecipientLanguage(string languageId)
    {
        // arrange
        var language = Language.Parse(languageId);
        var l = LanguageStringLocalizer.Get(language);
        var english = LanguageStringLocalizer.Get(Languages.English);

        // act
        var html = await Render<EmailVerification>(l, new() {
            { nameof(EmailVerification.Token), "123456" },
        });

        // assert
        html.Should().Contain("123456");
        html.Should().Contain($"lang=\"{language.IsoCode}\"");
        html.Should().Contain(l.EmailCode_Title);
        html.Should().Contain(l.EmailCode_Prompt_Format(CoreConstants.AppName));
        html.Should().Contain(l.EmailCode_Warning);
        html.Should().Contain(l.Documents_PrivacyPolicy);
        html.Should().Contain(l.Documents_TermsConditions);
        html.Should().NotContain(english.EmailCode_Title);
        html.Should().NotContain(english.EmailCode_Warning);
        html.Should().NotContain(english.Documents_PrivacyPolicy);
    }

    [Theory]
    [MemberData(nameof(TranslatedLanguages))]
    public async Task DigestShouldUseRecipientLanguage(string languageId)
    {
        // arrange
        var language = Language.Parse(languageId);
        var l = LanguageStringLocalizer.Get(language);
        var english = LanguageStringLocalizer.Get(Languages.English);

        // act
        var html = await Render<Digest>(l, new() {
            { nameof(Digest.Parameters), NewDigestParameters(unreadCount: 2, otherUnreadCount: 5) },
        });

        // assert
        html.Should().Contain($"lang=\"{language.IsoCode}\"");
        html.Should().Contain(l.EmailDigest_OpenUnread(2, 2, ChatName));
        html.Should().Contain(l.EmailDigest_OtherUnread(5, 5));
        html.Should().Contain(l.EmailDigest_Reason_Format(CoreConstants.AppName));
        html.Should().Contain(l.EmailDigest_TurnOff);
        html.Should().NotContain(english.EmailDigest_OpenUnread(2, 2, ChatName));
        html.Should().NotContain(english.EmailDigest_OtherUnread(5, 5));
        html.Should().NotContain(english.EmailDigest_TurnOff);
    }

    [Fact]
    public async Task DigestShouldInflectCountsForRecipientLanguage()
    {
        // arrange
        var l = LanguageStringLocalizer.Get(Languages.Russian);

        // act
        var html = await Render<Digest>(l, new() {
            { nameof(Digest.Parameters), NewDigestParameters(unreadCount: 3, otherUnreadCount: 21) },
        });

        // assert
        html.Should().Contain($"{ChatName}: 3 новых сообщения", "3 takes the 'few' form");
        html.Should().Contain("Ещё 21 непрочитанный чат", "21 takes the 'one' form");
    }

    [Fact]
    public async Task DigestShouldOfferChatWithoutCountWhenNothingIsUnread()
    {
        // arrange
        var l = LanguageStringLocalizer.Get(Languages.Spanish);

        // act
        var html = await Render<Digest>(l, new() {
            { nameof(Digest.Parameters), NewDigestParameters(unreadCount: 0, otherUnreadCount: 0) },
        });

        // assert
        html.Should().Contain(l.EmailDigest_OpenChat_Format(ChatName));
        html.Should().NotContain(l.EmailDigest_OtherUnread(0, 0));
    }

    // Private methods

    private static DigestParameters NewDigestParameters(long unreadCount, int otherUnreadCount)
        => new() {
            OtherUnreadCount = otherUnreadCount,
            OtherUnreadLink = "https://voxt.ai/",
            UnsubscribeLink = "https://voxt.ai/emails/digest/token/unsubscribe",
            UnreadChats = [
                new DigestParameters.DigestChat {
                    Name = ChatName,
                    Link = "https://voxt.ai/chat/x",
                    UnreadCount = unreadCount,
                    BulletPoints = ["Hello there"],
                },
            ],
        };

    // The renderer escapes everything outside Basic Latin, so the text is compared decoded
    private static async Task<string> Render<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
        IStringLocalizer l, Dictionary<string, object?> parameters)
        where T : IComponent
    {
        await using var renderer = new BlazorRenderer(l);
        var mjml = await renderer.RenderComponent<T>(parameters);
        var html = (await new MjmlRenderer().RenderAsync(mjml, new MjmlOptions { Beautify = false })).Html;
        return WebUtility.HtmlDecode(html);
    }
}
