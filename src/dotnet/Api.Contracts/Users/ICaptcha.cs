using ActualLab.Rpc;
using MemoryPack;
using MessagePack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record RecaptchaValidationResult(
    [property: MemoryPackOrder(0)] [property: Key(0)]
    bool Success,
    [property: MemoryPackOrder(1)] [property: Key(1)]
    string? ErrorMessage = null,
    [property: MemoryPackOrder(2)] [property: Key(2)]
    float? Score = null);

public interface ICaptcha : IRpcService
{
    Task<RecaptchaValidationResult> Validate(string token, string action, CancellationToken cancellationToken);
}
