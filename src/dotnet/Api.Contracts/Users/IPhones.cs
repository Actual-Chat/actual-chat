namespace ActualChat.Users;

public interface IPhones : IComputeService
{
    [ComputeMethod]
    Task<Phone> Parse(string sPhone, CancellationToken cancellationToken);
}
