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
        });
    }

    [Fact]
    public async Task ShouldEnrichImageRelativeUrl()
    {
        // arrange
        var html = await GetHtml("arxiv-image-relative-url");

        // act
        var graph = OpenGraphParser.Parse(html,  new Uri("https://arxiv.org/abs/2403.14403"));

        // assert
        graph.Should().Be(new OpenGraph("Adaptive-RAG: Learning to Adapt Retrieval-Augmented Large Language Models through Question Complexity") {
            Description = "Retrieval-Augmented Large Language Models (LLMs), which incorporate the non-parametric knowledge from external knowledge bases into LLMs, have emerged as a promising approach to enhancing response accuracy in several tasks, such as Question-Answering (QA). However, even though there are various approaches dealing with queries of different complexities, they either handle simple queries with unnecessary computational overhead or fail to adequately address complex multi-step queries; yet, not all user requests fall into only one of the simple or complex categories. In this work, we propose a novel adaptive QA framework, that can dynamically select the most suitable strategy for (retrieval-augmented) LLMs from the simplest to the most sophisticated ones based on the query complexity. Also, this selection process is operationalized with a classifier, which is a smaller LM trained to predict the complexity level of incoming queries with automatically collected labels, obtained from actual predicted outcomes of models and inherent inductive biases in datasets. This approach offers a balanced strategy, seamlessly adapting between the iterative and single-step retrieval-augmented LLMs, as well as the no-retrieval methods, in response to a range of query complexities. We validate our model on a set of open-domain QA datasets, covering multiple query complexities, and show that ours enhances the overall efficiency and accuracy of QA systems, compared to relevant baselines including the adaptive retrieval approaches. Code is available at: https://github.com/starsuzi/Adaptive-RAG.",
            ImageUrl = "https://arxiv.org/static/browse/0.3.4/images/arxiv-logo-fb.png",
            SiteName = "arXiv.org",
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
