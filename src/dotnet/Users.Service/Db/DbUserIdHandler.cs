using Stl.Conversion;
using Stl.Fusion.Authentication.Services;

namespace ActualChat.Users.Db;

public class DbUserIdHandler : DbUserIdHandler<string>
{
    public DbUserIdHandler(IConverterProvider converters)
        : base(converters, null)
        => Generator = () => UserId.New();
}
