using ActualChat.Testing.ApiContracts;

namespace ActualChat.Core.UnitTests;

public sealed class ApiContractCheckerTest(ITestOutputHelper @out) : TestBase(@out)
{
    private const string Header = "version 2.21\n";

    [Fact]
    public void ARemovedMethodShouldBeReported()
    {
        // arrange
        var old = Contract("method 0000000a IFoo.Get:2 (opaque:Session) -> prim:Boolean");
        var current = Contract("method 0000000b IFoo.List:2 (opaque:Session) -> list<prim:Int32>");

        // act
        var problems = Check(old, current);

        // assert
        problems.Should().ContainSingle().Which.Should().StartWith("IFoo.Get:2 endpoint: not found");
    }

    [Fact]
    public void ANilGapUnderAValueTypeShouldBeReported()
    {
        // arrange
        var old = Contract("""
            method 0000000a IFoo.Get:2 (opaque:Session) -> type:State
            object State
              0 Id: prim:String
              1 Outcome: enum:Outcome:Int32
              2 HasVideo: prim:Boolean
              3 Title: prim:String
            """);
        var current = Contract("""
            method 0000000a IFoo.Get:2 (opaque:Session) -> type:State
            object State
              0 Id: prim:String
              3 Title: prim:String
              4 HasTranscript: prim:Boolean
            """);

        // act
        var problems = Check(old, current);

        // assert
        problems.Should().HaveCount(2);
        problems.Should().Contain(p => p.Contains(".Outcome[1]: the writer leaves a nil gap"));
        problems.Should().Contain(p => p.Contains(".HasVideo[2]: the writer leaves a nil gap"));
    }

    [Fact]
    public void AMissingTrailingKeyOrANilTolerantGapShouldPass()
    {
        // arrange
        var old = Contract("""
            method 0000000a IFoo.Get:2 (opaque:Session) -> type:State
            object State
              0 Id: prim:String
              1 Note: prim:String
              2 Count: nullable<prim:Int32>
              3 Title: prim:String
              4 HasVideo: prim:Boolean
            """);
        var current = Contract("""
            method 0000000a IFoo.Get:2 (opaque:Session) -> type:State
            object State
              0 Id: prim:String
              3 Title: prim:String
            """);

        // act
        var problems = Check(old, current);

        // assert
        problems.Should().BeEmpty(
            "a nil gap is fine for a reference or Nullable<T>, and past the last key nothing is written");
    }

    [Fact]
    public void AChangedArgumentTypeShouldBeReported()
    {
        // arrange
        var old = Contract("method 0000000a IFoo.Cancel:3 (opaque:Session, opaque:ChatId) -> prim:Unit");
        var current = Contract("method 0000000a IFoo.Cancel:3 (opaque:Session, opaque:CallId) -> prim:Unit");

        // act
        var problems = Check(old, current);

        // assert
        problems.Should().ContainSingle().Which.Should().Contain("argument 1: opaque:ChatId is read as opaque:CallId");
    }

    [Fact]
    public void ALegacyMethodShouldBeComparedWithTheOldShape()
    {
        // arrange
        var old = Contract("""
            method 0000000a IFoo.Get:2 (opaque:Session) -> type:State
            object State
              0 Id: prim:String
            """);
        var current = Contract("""
            method 0000000b IFoo.Get:2 (opaque:Session) -> type:NewState
            method 0000000c IFoo.LegacyGet:2 (opaque:Session) -> type:LegacyState
            object NewState
              0 Version: prim:Int64
            object LegacyState
              0 Id: prim:String
            """);

        // act
        var problems = Check(old, current, hashCode => hashCode == 0xa ? "IFoo.LegacyGet:2" : null);

        // assert
        problems.Should().BeEmpty();
    }

    [Fact]
    public void AnUnknownUnionTagShouldBeReportedOnlyForAnIntolerantReader()
    {
        // arrange
        const string oldTypes = """
            union Item{0}
              0 type:Text
            object Text
              0 Value: prim:String
            """;
        const string currentTypes = """
            union Item
              0 type:Text
              1 type:Image
            object Text
              0 Value: prim:String
            object Image
              0 Url: prim:String
            """;
        const string method = "method 0000000a IFoo.Get:2 (opaque:Session) -> list<type:Item>\n";
        var current = Contract(method + currentTypes);

        // act
        var intolerant = Check(Contract(method + string.Format(oldTypes, "")), current);
        var tolerant = Check(Contract(method + string.Format(oldTypes, " tolerant")), current);

        // assert
        intolerant.Should().ContainSingle().Which.Should().Contain("result[]<1>: type:Image is a union member");
        tolerant.Should().BeEmpty();
    }

    [Fact]
    public void AContractShouldSurviveFormatAndParse()
    {
        // arrange
        var contract = Contract("""
            method 2e31840c ILiveSessions.GetMyCall:2 (opaque:ActualLab.Fusion.Session) -> type:UserCall
            object UserCall
              0 ChatId: opaque:ActualChat.ChatId
              1 Peers: map<prim:String, list<nullable<enum:Role:Int32>>>
            """);

        // act
        var text = contract.Format();
        var parsed = ApiContract.Parse(text);

        // assert
        parsed.Format().Should().Be(text);
        parsed.Methods.Single().HashCode.Should().Be(unchecked((int)0x2e31840cu));
    }

    // Private methods

    private List<string> Check(ApiContract old, ApiContract current, Func<int, string?>? resolve = null)
    {
        var currentByHash = current.Methods.ToDictionary(m => m.HashCode, m => m.FullName);
        resolve ??= hashCode => currentByHash.GetValueOrDefault(hashCode);
        var problems = new ApiContractChecker(old, current, resolve).Check().Select(p => p.ToString()).ToList();
        foreach (var problem in problems)
            Out.WriteLine(problem);
        return problems;
    }

    private static ApiContract Contract(string body)
        => ApiContract.Parse(Header + body);
}
