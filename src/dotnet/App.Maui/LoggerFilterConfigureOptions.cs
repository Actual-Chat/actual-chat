using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace ActualChat.App.Maui;

internal sealed class LoggerFilterConfigureOptions : IConfigureOptions<LoggerFilterOptions>
{
    private const string LogLevelKey = "LogLevel";
    private const string DefaultCategory = "Default";
    private readonly IConfiguration _configuration;

    public LoggerFilterConfigureOptions(IConfiguration configuration)
        => _configuration = configuration;

    public void Configure(LoggerFilterOptions options)
        => LoadDefaultConfigValues(options);

    private void LoadDefaultConfigValues(LoggerFilterOptions options)
    {
        options.CaptureScopes = GetCaptureScopesValue(options);

        foreach (IConfigurationSection configurationSection in _configuration.GetChildren()) {
            if (configurationSection.Key.Equals(LogLevelKey, StringComparison.OrdinalIgnoreCase)) {
                // Load global category defaults
                LoadRules(options, configurationSection, null);
            }
            else {
                IConfigurationSection logLevelSection = configurationSection.GetSection(LogLevelKey);
                if (logLevelSection != null) {
                    // Load logger specific rules
                    string logger = configurationSection.Key;
                    LoadRules(options, logLevelSection, logger);
                }
            }
        }

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode",
            Justification = "IConfiguration.GetValue is safe when T is a bool.")]
        bool GetCaptureScopesValue(LoggerFilterOptions options) => _configuration.GetValue(nameof(options.CaptureScopes), options.CaptureScopes);
    }

    private static void LoadRules(LoggerFilterOptions options, IConfigurationSection configurationSection, string? logger)
    {
        foreach (KeyValuePair<string, string?> section in configurationSection.AsEnumerable(true)) {
            if (!TryGetSwitch(section.Value, out LogLevel level))
                continue;
            string? category = section.Key;
            if (category.Equals(DefaultCategory, StringComparison.OrdinalIgnoreCase))
                category = null;
            var newRule = new LoggerFilterRule(logger, category, level, null);
            options.Rules.Add(newRule);
        }
    }

    private static bool TryGetSwitch(string? value, out LogLevel level)
    {
        if (value.IsNullOrEmpty()) {
            level = LogLevel.None;
            return false;
        }
        if (Enum.TryParse(value, true, out level))
            return true;
        throw new InvalidOperationException($"Value not supported: '{value}'");
    }
}
