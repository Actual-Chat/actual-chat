using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using ActualChat.MLSearch.Bot.External;

namespace ActualChat.MLSearch.Module;

public sealed class MLIntegrations
{
    /// <summary>
    /// The path for the PEM-encoded X509 certificate
    /// </summary>
    public required string CertPemFilePath { get; set; }

    /// <summary>
    /// The path for the PEM-encoded private key
    /// </summary>
    public required string KeyPemFilePath { get; set; }

    /// <summary>
    /// For example: "integrations.actual.chat"
    /// </summary>
    // Note:
    // There's an Issuer field in the certificate exists. 
    // However for all other fields it requires to use
    // X.509 v3 extensions. And it complicates things a lot.
    // Therefore adding those explicitly into the config.
    // (Till a better solution proposed)
    public required string Issuer { get; set; }

    /// <summary>
    /// For example: "bot-tools.actual.chat"
    /// </summary>
    public required string Audience { get; set; }

    /// <summary>
    /// For example "0.00:05:00"
    /// </summary>
    public required TimeSpan ContextLifetime { get; set; }

    public ExternalChatbotSettings? Bot { get; set; }
}