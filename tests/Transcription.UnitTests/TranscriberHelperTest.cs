namespace ActualChat.Transcription.UnitTests;

// The silence resource is loaded by name from the assembly that hosts TranscriberHelper,
// so moving either the type or the file between projects breaks it at runtime only.
public class TranscriberHelperTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void SilenceResourceIsEmbedded()
    {
        // act
        var assembly = typeof(TranscriberHelper).Assembly;
        var names = assembly.GetManifestResourceNames();

        // assert
        WriteLine($"{assembly.GetName().Name}: {names.ToDelimitedString()}");
        names.Should().Contain(x => x.EndsWith("silence.opuss"));
    }
}
