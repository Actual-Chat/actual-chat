namespace ActualChat;

public static class AssemblyExt
{
    public static string GetContentUrl(this Assembly assembly, string relativePath)
        => Path.Combine($"./_content/{assembly.GetName().Name}/", relativePath);

    public static string? GetInformationalVersion(this Assembly assembly)
    {
        var attributeDef = assembly.CustomAttributes.FirstOrDefault(
                d => d.AttributeType == typeof(AssemblyInformationalVersionAttribute));
        return attributeDef?.ConstructorArguments.FirstOrDefault().Value as string;
    }
}
