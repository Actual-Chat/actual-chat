namespace BlazorContextMenu;

public class BlazorContextMenuSettingsBuilder
{
    private BlazorContextMenuSettings Settings { get; }

    public BlazorContextMenuSettingsBuilder(BlazorContextMenuSettings settings)
        => Settings = settings;

    /// <summary>
    /// Configures the default template.
    /// </summary>
    /// <param name="templateOptions"></param>
    /// <returns></returns>
    public BlazorContextMenuSettingsBuilder ConfigureTemplate(Action<BlazorContextMenuTemplate> templateOptions)
    {
        var template = Settings.GetTemplate(BlazorContextMenuSettings.DefaultTemplateName);
        templateOptions(template);
        return this;
    }

    /// <summary>
    /// Configures a named template.
    /// </summary>
    /// <param name="templateName"></param>
    /// <param name="templateOptions"></param>
    /// <returns></returns>
    public BlazorContextMenuSettingsBuilder ConfigureTemplate(string templateName,Action<BlazorContextMenuTemplate> templateOptions)
    {
        if (Settings.Templates.ContainsKey(templateName)) throw new Exception($"Template '{templateName}' is already defined");
        var template = new BlazorContextMenuTemplate();
        templateOptions(template);

        Settings.Templates.Add(templateName, template);
        return this;
    }

}
