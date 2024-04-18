namespace ActualChat.Users;

public interface IPhones : IComputeService
{
    // NOTE(AY): Should it really be a compute method? Let's discuss this.
    [ComputeMethod]
    Task<Phone> Parse(string phone, CancellationToken cancellationToken);
}
