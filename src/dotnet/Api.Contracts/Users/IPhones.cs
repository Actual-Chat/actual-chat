namespace ActualChat.Users;

/// <summary>
/// Service for parsing and validating phone numbers.
/// </summary>
public interface IPhones : IComputeService
{
    // NOTE(AY): Should it really be a compute method?
    [ComputeMethod]
    Task<Phone?> Parse(string phone, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<Phone?> GetExampleCountryPhone(Session session, CancellationToken cancellationToken);
}
