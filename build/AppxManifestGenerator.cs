using System.Diagnostics;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace Build;

public static class AppxManifestGenerator
{
    public static async Task Generate(bool isProduction, CancellationToken cancellationToken)
    {
        var version= await GetVersion(cancellationToken);
        await Generate($"{version.ToString(3)}.0",
            isProduction ? "" : ".Dev",
            isProduction ? "" : " Dev",
            cancellationToken);
    }

    private static async Task<Version> GetVersion(CancellationToken cancellationToken)
    {
        var process = Process.Start(new ProcessStartInfo(Utils.FindDotnetExe(), "nbgv get-version -v NuGetPackageVersion") {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        });
        if (process?.HasExited != false)
            throw new InvalidOperationException("Failed to start run nbgv command.");

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Failed to get version via nbgv command: {error}. {output}");

        return Version.Parse(output);
    }

    public static async Task Generate(
        string version,
        string packageIdentityNameSuffix,
        string startupTaskDisplayNameSuffix,
        CancellationToken cancellationToken)
    {
        var manifestPath = "src/dotnet/App.Maui/Platforms/Windows/Package.appxmanifest";
        var (doc, xns) = await Read(manifestPath, cancellationToken);

        if (!string.IsNullOrEmpty(version))
            UpdateAttr("//default:Package/default:Identity", "Version",version);
        UpdateAttr("//default:Package/default:Identity", "Name",$"ActualChatInc.ActualChat{packageIdentityNameSuffix}");
        UpdateAttr("//uap5:StartupTask", "DisplayName", $"Actual Chat{startupTaskDisplayNameSuffix}");

        await Write(manifestPath, doc, cancellationToken);

        void UpdateAttr(string xpath, string attrName, string attrValue)
        {
            var element = doc.XPathSelectElement(xpath, xns);
            element!.SetAttributeValue(attrName, attrValue);
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
        nsManager.AddNamespace("default", "http://schemas.microsoft.com/appx/manifest/foundation/windows10");
        nsManager.AddNamespace("uap5", "http://schemas.microsoft.com/appx/manifest/uap/windows10/5");
        return (xDocument, nsManager);
    }
}
