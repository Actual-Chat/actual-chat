using ActualChat.App.AotHelper;
using static System.Console;

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

if (generateMode)
    return AotTypeGenerator.Generate(projectRoot);

return AotTypeTester.RunTests();
