namespace ActualChat.Testing.ApiContracts;

// Location is where in the call it breaks: "endpoint", or a path such as "result.Outcome[22]";
// ServedBy names the method the server runs for that id when it differs from Method

public sealed record ApiContractProblem(string Method, string Location, string Message, string? ServedBy = null)
{
    public override string ToString()
        => ServedBy is null
            ? $"{Method} {Location}: {Message}"
            : $"{Method} {Location}: {Message} (served by {ServedBy})";
}

/// <summary>
/// Finds what breaks for a client of <c>oldContract</c>'s release talking to the server behind
/// <c>currentContract</c>: an id nothing answers, or an argument or result one side can't read.
/// <c>resolveMethodName</c> maps a wire id to the method the server runs for that client.
/// </summary>
public sealed class ApiContractChecker(
    ApiContract oldContract,
    ApiContract currentContract,
    Func<int, string?> resolveMethodName)
{
    private readonly Dictionary<(string, string, bool), List<string>> _definedProblems = new();

    private ApiContract OldContract { get; } = oldContract;
    private ApiContract CurrentContract { get; } = currentContract;
    private Func<int, string?> ResolveMethodName { get; } = resolveMethodName;

    public IReadOnlyList<ApiContractProblem> Check()
    {
        var currentMethods = CurrentContract.Methods.ToDictionary(m => m.FullName);
        var problems = new List<ApiContractProblem>();
        foreach (var oldMethod in OldContract.Methods) {
            var oldName = oldMethod.FullName;
            var name = ResolveMethodName(oldMethod.HashCode);
            if (name is null) {
                problems.Add(new(oldName, "endpoint", "not found - the server has no method for this id"));
                continue;
            }

            var servedBy = name == oldName ? null : name;
            if (!currentMethods.TryGetValue(name, out var method)) {
                problems.Add(new(oldName, "endpoint", "resolves to a method that isn't client-facing", servedBy));
                continue;
            }

            if (method.Arguments.Count != oldMethod.Arguments.Count) {
                var message = $"argument count differs, {oldMethod.Arguments.Count} vs {method.Arguments.Count}";
                problems.Add(new(oldName, "endpoint", message, servedBy));
                continue;
            }

            for (var i = 0; i < method.Arguments.Count; i++) {
                // Arguments go from the old client to the current server
                var argumentProblems = Compare(oldMethod.Arguments[i], method.Arguments[i], isOldWriter: true);
                problems.AddRange(argumentProblems.Select(p => ToProblem(oldName, $"argument {i}", p, servedBy)));
            }
            var resultProblems = Compare(method.Result, oldMethod.Result, isOldWriter: false);
            problems.AddRange(resultProblems.Select(p => ToProblem(oldName, "result", p, servedBy)));
        }
        return problems;
    }

    // Private methods

    private List<string> Compare(ApiTypeRef writer, ApiTypeRef reader, bool isOldWriter)
    {
        var (writerContract, readerContract) = isOldWriter
            ? (OldContract, CurrentContract)
            : (CurrentContract, OldContract);
        if (writer.Kind == ApiTypeRefKind.Any || reader.Kind == ApiTypeRefKind.Any)
            return [];
        if (reader.Kind == ApiTypeRefKind.Nullable)
            return Compare(Unwrap(writer), reader.Args[0], isOldWriter);

        if (writer.Kind == ApiTypeRefKind.Nullable) {
            var problems = Compare(writer.Args[0], reader, isOldWriter);
            if (!AcceptsNil(reader, readerContract))
                problems.Insert(0, $": a nil the writer may send can't be read as {reader}");
            return problems;
        }

        if (writer.Kind == ApiTypeRefKind.Enum && reader.Kind == ApiTypeRefKind.Primitive)
            return writer.EnumUnderlyingName == reader.Name ? [] : [$": {writer} is read as {reader}"];
        if (writer.Kind == ApiTypeRefKind.Primitive && reader.Kind == ApiTypeRefKind.Enum)
            return writer.Name == reader.EnumUnderlyingName ? [] : [$": {writer} is read as {reader}"];
        if (writer.Kind != reader.Kind)
            return [$": {writer} is read as {reader}"];

        switch (reader.Kind) {
        case ApiTypeRefKind.Enum:
            return writer.EnumUnderlyingName == reader.EnumUnderlyingName ? [] : [$": {writer} is read as {reader}"];
        case ApiTypeRefKind.List:
            return Compare(writer.Args[0], reader.Args[0], isOldWriter).ConvertAll(p => "[]" + p);
        case ApiTypeRefKind.Map:
            var keyProblems = Compare(writer.Args[0], reader.Args[0], isOldWriter).Select(p => "{key}" + p);
            var valueProblems = Compare(writer.Args[1], reader.Args[1], isOldWriter).Select(p => "{value}" + p);
            return [..keyProblems, ..valueProblems];
        case ApiTypeRefKind.Defined:
            return CompareDefined(
                writerContract.Types.GetValueOrDefault(writer.Name),
                readerContract.Types.GetValueOrDefault(reader.Name),
                isOldWriter);
        default:
            if (writer.Name != reader.Name || writer.Args.Count != reader.Args.Count)
                return [$": {writer} is read as {reader}"];

            return writer.Args
                .SelectMany((arg, i) => Compare(arg, reader.Args[i], isOldWriter).Select(p => $"<{i}>{p}"))
                .ToList();
        }
    }

    private List<string> CompareDefined(ApiTypeShape? writer, ApiTypeShape? reader, bool isOldWriter)
    {
        if (writer is null || reader is null)
            return [$": no shape recorded for {writer?.Name ?? reader?.Name}"];

        var key = (writer.Name, reader.Name, isOldWriter);
        if (_definedProblems.TryGetValue(key, out var cached))
            return [..cached];

        // A recursive type reaches itself again with this empty list, which stops the descent
        var problems = new List<string>();
        _definedProblems[key] = problems;
        problems.AddRange(writer.Kind == ApiTypeKind.Union || reader.Kind == ApiTypeKind.Union
            ? CompareUnions(writer, reader, isOldWriter)
            : CompareObjects(writer, reader, isOldWriter));
        return [..problems];
    }

    private List<string> CompareUnions(ApiTypeShape writer, ApiTypeShape reader, bool isOldWriter)
    {
        if (writer.Kind != reader.Kind)
            return [$": {writer.Kind} {writer.Name} is read as {reader.Kind} {reader.Name}"];

        var problems = new List<string>();
        var readerTags = reader.Members.ToDictionary(m => m.Key);
        foreach (var writerTag in writer.Members) {
            var path = $"<{writerTag.Key}>";
            if (readerTags.TryGetValue(writerTag.Key, out var readerTag))
                problems.AddRange(Compare(writerTag.Type, readerTag.Type, isOldWriter).Select(p => path + p));
            else if (!reader.IsTolerant)
                problems.Add($"{path}: {writerTag.Type} is a union member {reader.Name} doesn't know");
        }
        return problems;
    }

    private List<string> CompareObjects(ApiTypeShape writer, ApiTypeShape reader, bool isOldWriter)
    {
        if (writer.IsMap != reader.IsMap)
            return [$": {writer.Name} and {reader.Name} differ in layout - one has string keys"];

        var problems = new List<string>();
        var readerContract = isOldWriter ? CurrentContract : OldContract;
        var writerMembers = writer.Members.ToDictionary(m => m.Key);
        var writerMaxIntKey = writer.MaxIntKey;
        foreach (var member in reader.Members) {
            var path = $".{member.Name}[{member.Key}]";
            if (writerMembers.TryGetValue(member.Key, out var writerMember))
                problems.AddRange(Compare(writerMember.Type, member.Type, isOldWriter).Select(p => path + p));
            else if (!reader.IsMap
                && int.Parse(member.Key) < writerMaxIntKey
                && !AcceptsNil(member.Type, readerContract))
                problems.Add($"{path}: the writer leaves a nil gap at this key, which {member.Type} can't read");
        }
        return problems;
    }

    private static ApiContractProblem ToProblem(string method, string location, string problem, string? servedBy)
    {
        // A problem from Compare is "<path below the location>: <message>"
        var separator = problem.IndexOf(": ");
        return new ApiContractProblem(method, location + problem[..separator], problem[(separator + 2)..], servedBy);
    }

    private static ApiTypeRef Unwrap(ApiTypeRef type)
        => type.Kind == ApiTypeRefKind.Nullable ? type.Args[0] : type;

    private static bool AcceptsNil(ApiTypeRef type, ApiContract contract)
        => type.Kind == ApiTypeRefKind.Defined
            ? contract.Types.GetValueOrDefault(type.Name) is not { IsStruct: true }
            : type.AcceptsNil;
}
