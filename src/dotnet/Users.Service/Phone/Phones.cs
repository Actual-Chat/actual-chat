using ActualChat.Core.NonWasm;

namespace ActualChat.Users;

public class Phones : IPhones
{
    // [ComputeMethod]
    public virtual Task<Phone> Parse(string sPhone, CancellationToken cancellationToken)
        => Task.FromResult(PhoneFormatterExt.FromReadable(sPhone));
}
