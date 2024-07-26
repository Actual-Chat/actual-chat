using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using ActualChat.MLSearch.Bot.External;

namespace ActualChat.MLSearch.Module;

public sealed class MLIntegrations
{
    // TODO:
    // Note: This must be changed.
    // No need to generate an actual X509Certificate2 for this usecase.
    public required string CertPemFilePath { get; set; }
    
    public required string KeyPemFilePath { get; set; }

    public ExternalChatbotSettings? Bot { get; set; }
}