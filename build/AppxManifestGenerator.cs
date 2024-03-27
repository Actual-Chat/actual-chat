using System.Diagnostics;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace Build;

public static class AppxManifestGenerator
{
    public static async Task Generate(bool isProduction, CancellationToken cancellationToken)
    {
        var version= await GetVersion(cancellationToken).ConfigureAwait(false);
        var appVersion = $"{version.ToString(3)}.0";
        await Generate(appVersion,
            isProduction ? "" : ".Dev",
            isProduction ? "" : " Dev",
            cancellationToken).ConfigureAwait(false);
        Utils.SetGithubOutput("AppVersion", appVersion);
    }

    private static async Task<Version> GetVersion(CancellationToken cancellationToken)
    {
        var process = Process.Start(new ProcessStartInfo(Utils.FindDotnetExe(), "nbgv get-version -v Version") {
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
        var (doc, xns) = await Read(manifestPath, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(version))
            UpdateAttr("//default:Package/default:Identity", "Version",version);
        UpdateAttr("//default:Package/default:Identity", "Name",$"ActualChatInc.ActualChat{packageIdentityNameSuffix}");
        UpdateAttr("//uap5:StartupTask", "DisplayName", $"Actual Chat{startupTaskDisplayNameSuffix}");

        await Write(manifestPath, doc, cancellationToken).ConfigureAwait(false);

        void UpdateAttr(string xpath, string attrName, string attrValue)
        {
            var element = doc.XPathSelectElement(xpath, xns);
            element!.SetAttributeValue(attrName, attrValue);
        }
    }

    private static async Task Write(string manifestPath, XDocument doc, CancellationToken cancellationToken)
    {
        File.Delete(manifestPath);
        var output = File.OpenWrite(manifestPath);
        await using var _ = output.ConfigureAwait(false);
        await doc.SaveAsync(output, SaveOptions.DisableFormatting, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(XDocument Doc, XmlNamespaceManager Xns)> Read(string manifestPath, CancellationToken cancellationToken)
    {
        var input = File.OpenRead(manifestPath);
        await using var _ = input.ConfigureAwait(false);
        var xDocument = await XDocument.LoadAsync(input, LoadOptions.PreserveWhitespace, cancellationToken).ConfigureAwait(false);
        var nsManager = new XmlNamespaceManager(new NameTable());
        nsManager.AddNamespace("", "http://schemas.microsoft.com/appx/manifest/foundation/windows10");
        nsManager.AddNamespace("default", "http://schemas.microsoft.com/appx/manifest/foundation/windows10");
        nsManager.AddNamespace("uap5", "http://schemas.microsoft.com/appx/manifest/uap/windows10/5");
        return (xDocument, nsManager);
    }
}
