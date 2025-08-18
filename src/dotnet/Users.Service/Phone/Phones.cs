using PhoneNumbers;

namespace ActualChat.Users.Phone;

public class Phones : IPhones
{
    // [ComputeMethod]
    public virtual Task<ActualChat.Phone?> Parse(string phone, CancellationToken cancellationToken)
        => Task.FromResult(PhoneExt.ParseNullable(phone, null));
}
