using ActualChat.App.AotHelper;

// Parse arguments
var generateMode = false;
string? projectRoot = null;
string? mibcPath = null;

for (var i = 0; i < args.Length; i++) {
    if (args[i] is "-g" or "--generate") {
        generateMode = true;
        if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
            projectRoot = args[++i];
    }
    else if (args[i] is "-m" or "--mibc") {
        mibcPath = i + 1 < args.Length && !args[i + 1].StartsWith('-')
            ? args[++i]
            : "aothelper.mibc";
    }
}

if (mibcPath != null)
    return MibcGenerator.Generate(mibcPath);

return generateMode
    ? AotTypeGenerator.Generate(projectRoot)
    : AotTypeTester.RunTests();
