using PhoneNumbers;

namespace ActualChat.Users;

public class Phones : IPhones
{
    // [ComputeMethod]
    public virtual Task<Phone> Parse(string phone, CancellationToken cancellationToken)
        => Task.FromResult(PhoneExt.Parse(phone, null));
}
