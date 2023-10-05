using Microsoft.Toolkit.HighPerformance;

namespace ActualChat.Core.UnitTests.Text;

public class HashExtTest
{
    [Fact]
    public void EmptyHashCodeTest()
    {
        "".GetDjb2HashCode();
        "".GetSHA1HashCode();
        "".GetSHA256HashCode();
    }
}
