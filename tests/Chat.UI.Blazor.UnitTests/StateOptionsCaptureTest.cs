using System.Reflection;
using ActualChat.UI.Blazor.App.Components;
using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class StateOptionsCaptureTest
{
    // ComputedStateComponent.GetStateOptions(Type, factory) caches the options per component type, so a
    // factory that reads the instance hands the first instance's InitialValue to every later one (#4735).
    // The compiler shows the capture: a lambda over `this` becomes a method of the component itself, one
    // over locals a method of a <>c__DisplayClass; a lambda capturing nothing lives in <>c. The factory is
    // told apart from other GetStateOptions lambdas by its Func<Type, options> shape.
    private const string LambdaPrefix = "<GetStateOptions>b__";

    [Fact]
    public void GetStateOptionsLambdasShouldNotCaptureTheComponent()
    {
        // arrange
        var app = typeof(ChatSidePanel).Assembly;
        var components = app.GetReferencedAssemblies()
            .Where(a => a.Name!.StartsWith("ActualChat.", StringComparison.Ordinal))
            .Select(Assembly.Load)
            .Prepend(app)
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(ComputedStateComponent).IsAssignableFrom(t))
            .ToList();
        components.Select(t => t.Assembly.GetName().Name).Distinct()
            .Should().Contain(["ActualChat.UI.Blazor", "ActualChat.UI.Blazor.App"],
                "both UI assemblies hold computed-state components, so both must be scanned");

        // act
        var capturing = components
            .SelectMany(t => t.GetNestedTypes(BindingFlags.NonPublic)
                .Where(n => n.Name.StartsWith("<>c__DisplayClass", StringComparison.Ordinal))
                .Prepend(t))
            .SelectMany(t => t.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(m => m.Name.StartsWith(LambdaPrefix, StringComparison.Ordinal)
                && m.GetParameters() is [{ ParameterType: var p }] && p == typeof(Type)
                && typeof(IComputedStateOptions).IsAssignableFrom(m.ReturnType))
            .Select(m => $"{m.DeclaringType!.FullName}.{m.Name}")
            .ToList();

        // assert
        string.Join(Environment.NewLine, capturing).Should().BeEmpty(
            "a GetStateOptions factory passed to the per-type cache must be static; "
            + "options built from instance data belong in `=> new() { ... }`");
    }
}
