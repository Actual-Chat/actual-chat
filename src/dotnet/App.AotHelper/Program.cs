using ActualChat.App.AotHelper;

// Parse arguments
var generateMode = false;
string? projectRoot = null;

for (var i = 0; i < args.Length; i++) {
    if (args[i] is "-g" or "--generate") {
        generateMode = true;
        if (i + 1 < args.Length)
            projectRoot = args[++i];
    }
}

return generateMode
    ? AotTypeGenerator.Generate(projectRoot)
    : AotTypeTester.RunTests();
