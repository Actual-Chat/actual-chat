using System.ComponentModel.DataAnnotations;

namespace ActualChat.MLSearch.Module;

public sealed class MLSearchSettings
{
    // Section names
    public const string OpenSearch = nameof(OpenSearch);

    // Root config properties
    public bool IsEnabled { get; set; }
    public string Db { get; set; } = "";
    public string Redis { get; set; } = "";

    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan ChangedEntityIndexingDelay { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan IndexingFlowResumeDelayQuanta { get; set; } = TimeSpan.FromMinutes(1);
    public string OpenSearchNamesEnvPrefix { get; set; } = "";
}

public sealed class OpenSearchSettings
{
    [Required, Uri]
    public string ClusterUri { get; set; } = "";

    [Range(0, 4, ErrorMessage = "Value for {0} must be between {1} and {2}.")]
    public int? DefaultNumberOfReplicas { get; set; }

    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string ClientCertificatePath { get; set; } = "";
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class UriAttribute(): ValidationAttribute("Value for {0} must be a valid URI.")
{
    public override bool IsValid(object? value) => value is string valueAsString
        && !valueAsString.IsNullOrWhiteSpace() && Uri.IsWellFormedUriString(valueAsString, UriKind.Absolute);
}
