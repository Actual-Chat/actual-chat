
using System.ComponentModel.DataAnnotations;

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
}

public sealed class OpenSearchSettings
{
    [Required]
    public string ClusterUri { get; set; } = "";

    [Required]
    [RegularExpression(@"^[^\s]+$", ErrorMessage = "Value for {0} must be non-empty string of word characters.")]
    public string ModelGroup { get; set; } = "";

    [Range(0, 4, ErrorMessage = "Value for {0} must be between {1} and {2}.")]
    public int? DefaultNumberOfReplicas { get; set; }
}
