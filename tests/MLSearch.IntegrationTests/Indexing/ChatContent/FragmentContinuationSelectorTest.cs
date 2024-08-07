using ActualChat.MLSearch.Indexing.ChatContent;

namespace ActualChat.MLSearch.IntegrationTests.Indexing.ChatContent;

public class FragmentContinuationSelectorTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact(Skip = "Run explicitly")]
    public async Task ChooseOptionTest()
    {
        var selector = new DialogFragmentAnalyzer(Log);
        var index = await selector.ChooseOption(
            "Extensive evaluation will show you a standard RAG pipeline is certainly not enough to avoid unexpected hallucinations, overlooked knowledge, and misunderstood context.",
            new string[] {
                "Install the latest version of Go. For instructions to download and install the Go compilers, tools, and libraries, view the install documentation.",
                "Retrieval Augmented Generation (RAG) has emerged as a needed framework for improving the quality of LLM-generated responses. Without RAG, Large Language Models only have access to the knowledge contained in their training data. With the inclusion of RAG, LLMs can improve prediction quality by tapping into external data sources, building a prompt that is loaded with rich context and relevant knowledge. But is the use of standard RAG enough of a quality improvement on generated output to use in production applications?"
            });
        index.Should().Be(1);
    }
}
