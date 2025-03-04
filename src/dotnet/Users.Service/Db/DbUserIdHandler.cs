using ActualLab.Conversion;
using ActualLab.Fusion.Authentication.Services;

namespace ActualChat.Users.Db;

[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbUserIdHandler : DbUserIdHandler<string>
{
    public DbUserIdHandler(IConverterProvider converters)
        : base(converters, null)
        => Generator = () => UserId.New();
}
