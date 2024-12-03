
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace ActualChat.MLSearch.Module;

public sealed class MLSearchSettings
{
    // Section names
    public const string OpenSearch = nameof(OpenSearch);

    // Root config properties
    public bool IsEnabled { get; set; }
    public bool IsInitialIndexingDisabled { get; set; }
    public string Db { get; set; } = "";
    public string Redis { get; set; } = "";

    public ChatbotSettings? Bot { get; set; }
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan IndexingDelay { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan IndexingRecheckInterval { get; set; } = TimeSpan.FromSeconds(16);
    public TimeSpan ContactIndexingSignalInterval { get; set; } = TimeSpan.FromSeconds(1); // TODO: remove when migration to flows complete
    public string OpenSearchNamesEnvPrefix { get; set; } = "";
}

public sealed class OpenAISettings
{
    public string ChatModel { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class ChatbotSettings {
    public bool IsEnabled { get; set; }
    public bool AllowPeerBotChat { get; set; }
    public TimeSpan ConversationTtl { get; set; } = TimeSpan.FromHours(1);
    public OpenAISettings? OpenAI { get; set; }
}

public sealed class OpenSearchSettings
{
    [Required, Uri]
    public string ClusterUri { get; set; } = "";

    [Required]
    [RegularExpression(@"^[^\s]+$", ErrorMessage = "Value for {0} must be non-empty string of word characters.")]
    public string ModelGroup { get; set; } = "";

    [Range(0, 4, ErrorMessage = "Value for {0} must be between {1} and {2}.")]
    public int? DefaultNumberOfReplicas { get; set; }

    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string ClientCertificatePath { get; set; } = "";

    [ValidateObjectMembers]
    public EmbeddingServiceSettings EmbeddingService { get; set; } = new();
}

public enum EmbeddingServiceType
{
    BuiltIn,
    Custom
}

public sealed class EmbeddingServiceSettings
{
    public EmbeddingServiceType EmbeddingServiceType { get; set; }

    [ValidateObjectMembers]
    public CustomEmbeddingServiceSettings? Custom { get; set;}
}

public sealed class CustomEmbeddingServiceSettings
{
    [Required, Uri]
    public string Uri { get; set; } = "";

    [Required, Uri]
    public string ManagementUri { get; set; } = "";
    public string? ModelName { get; set; }
}

public sealed class UriAttribute(): ValidationAttribute("Value for {0} must be a valid URI.")
{
    public override bool IsValid(object? value) => value is string valueAsString
        && !string.IsNullOrWhiteSpace(valueAsString) && Uri.IsWellFormedUriString(valueAsString, UriKind.Absolute);
}
