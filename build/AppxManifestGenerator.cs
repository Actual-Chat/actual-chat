using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace Build;

public static class AppxManifestGenerator
{
    public static Task Generate(bool isProduction, CancellationToken cancellationToken)
        => Generate("",
            isProduction ? "" : ".Dev",
            isProduction ? "" : " Dev",
            cancellationToken);

    public static async Task Generate(
        string version,
        string packageIdentityNameSuffix,
        string startupTaskDisplayNameSuffix,
        CancellationToken cancellationToken)
    {
        var manifestPath = "src/dotnet/App.Maui/Platforms/Windows/Package.appxmanifest";
        var (doc, xns) = await Read(manifestPath, cancellationToken);

        if (!string.IsNullOrEmpty(version))
            UpdateAttr("//Package/Identity", "Version",version);
        UpdateAttr("//Package/Identity", "Name",$"ActualChatInc.ActualChat{packageIdentityNameSuffix}");
        UpdateAttr("//uap5:StartupTask", "DisplayName", $"Actual Chat{startupTaskDisplayNameSuffix}");

        await Write(manifestPath, doc, cancellationToken);

        void UpdateAttr(string xpath, string attrName, string attrValue)
        {
            var element = doc.XPathSelectElement(xpath, xns);
            element?.SetAttributeValue(attrName, attrValue);
        }
    }

    private static async Task Write(string manifestPath, XDocument doc, CancellationToken cancellationToken)
    {
        File.Delete(manifestPath);
        await using var output = File.OpenWrite(manifestPath);
        await doc.SaveAsync(output, SaveOptions.DisableFormatting, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(XDocument Doc, XmlNamespaceManager Xns)> Read(string manifestPath, CancellationToken cancellationToken)
    {
        await using var input = File.OpenRead(manifestPath);
        var xDocument = await XDocument.LoadAsync(input, LoadOptions.PreserveWhitespace, cancellationToken);
        var nsManager = new XmlNamespaceManager(new NameTable());
        nsManager.AddNamespace("", "http://schemas.microsoft.com/appx/manifest/foundation/windows10");
        nsManager.AddNamespace("uap5", "http://schemas.microsoft.com/appx/manifest/uap/windows10/5");
        return (xDocument, nsManager);
    }
}
