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

    private async Task<string> GetHtml(string testCase)
    {
        var type = GetType();
        await using var htmlStream = type.Assembly.GetManifestResourceStream($"{type.Namespace}.TestPages.{testCase}.html").Require();
        var html = await new StreamReader(htmlStream).ReadToEndAsync();
        return html;
    }
}
