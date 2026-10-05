using ActualLab.Rpc;

namespace ActualChat.Users;

public interface ITotpCodesBackend : IRpcService, IBackendService
{
    Task<int> Generate(string target, TotpPurpose purpose, CancellationToken cancellationToken = default);
    Task<bool> Validate(string target, TotpPurpose purpose, int code, CancellationToken cancellationToken = default);

    Task<bool> IsEmailThrottled(string email, CancellationToken cancellationToken);
    Task<bool> IsPhoneThrottled(Phone phone, CancellationToken cancellationToken);
    Task<TotpChannel?> GetLastChannel(Phone phone, CancellationToken cancellationToken);
    Task SetLastChannel(Phone phone, TotpChannel channel, CancellationToken cancellationToken);
}
