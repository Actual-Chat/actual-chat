namespace ActualChat.Media.UnitTests;

public class OpenGraphParserTest
{
    [Fact]
    public async Task ShouldReturnNullIfNoTitle()
    {
        // arrange
        var html = await GetHtml("github-actualchat");

        // act
        var graph = OpenGraphParser.Parse(html);

        // assert
        graph.Should().Be(new OpenGraph("GitHub - Actual-Chat/actual-chat") {
            SiteName = "GitHub",
            ImageUrl = "https://opengraph.githubassets.com/37ab60b2fbc9dcb5f52752298b7be4939a77de41e019e2380c059bfa9b05b4a7/Actual-Chat/actual-chat",
            Description = "Contribute to Actual-Chat/actual-chat development by creating an account on GitHub.",
        });
    }

    [Fact]
    public async Task ShouldGetFromTitleTag()
    {
        // arrange
        var html = await GetHtml("title-fallback");

        // act
        var graph = OpenGraphParser.Parse(html);

        // assert
        graph.Should().Be(new OpenGraph("Default title"));
    }

    [Fact]
    public async Task ShouldParseVideoMeta()
    {
        // arrange
        var html = await GetHtml("rag-youtube");

        // act
        var graph = OpenGraphParser.Parse(html);

        // assert
        graph.Should().Be(new OpenGraph("Vector Search RAG Tutorial – Combine Your Data with LLMs with Advanced Search") {
            Description = "Learn how to use vector search and embeddings to easily combine your data with large language models like GPT-4. You will first learn the concepts and then c...",
            ImageUrl = "https://i.ytimg.com/vi/JEBDfGqrAUA/maxresdefault.jpg",
            SiteName = "YouTube",
            Video = new () {
                SecureUrl = "https://www.youtube.com/embed/JEBDfGqrAUA",
                Height = 720,
                Width = 1280,
            },
        });
    }

    [Fact]
    public async Task ShouldDecodeImageUrl()
    {
        // arrange
        var html = await GetHtml("reuters-image-decode");

        // act
        var graph = OpenGraphParser.Parse(html);

        // assert
        graph.Should().Be(new OpenGraph("US probes Tesla recall of 2 million vehicles over Autopilot") {
            Description = "U.S. auto safety regulators said on Friday they have opened an investigation into whether Tesla's recall of more than 2 million vehicles announced in December to install new Autopilot safeguards is adequate following a series of crashes.",
            ImageUrl = "https://www.reuters.com/resizer/v2/WSUHVXIY5NNDXBWKP4ZFQQM3LM.jpg?auth=d6851bae48fc48e6b42940e7d665e8a9feec4f426815a52a9cbd92fd7d025336&height=1005&width=1920&quality=80&smart=true",
            SiteName = "Reuters",
        });;
    }

    private async Task<string> GetHtml(string testCase)
    {
        var type = GetType();
        await using var htmlStream = type.Assembly.GetManifestResourceStream($"{type.Namespace}.TestPages.{testCase}.html").Require();
        var html = await new StreamReader(htmlStream).ReadToEndAsync();
        return html;
    }
}
