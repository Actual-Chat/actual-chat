namespace ActualChat.UI.Blazor;

/// <summary>
/// Contains variable names of the webpack outputs <br />
/// Must be in sync with <c>webpack.config.js</c>
/// </summary>
public static class WebpackBundles
{
    /// <summary>
    /// The variable name of the webpack main bundle<br />
    /// <example>
    /// Example: <c>js</c> is a bundle name. <br />
    /// <c>let obj = new js.moduleName.moduleExportObj();</c>
    /// </example>
    /// </summary>
    public const string Main = "js";
}
