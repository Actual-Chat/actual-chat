using Stl.Conversion;
using Stl.Fusion.EntityFramework.Authentication;

namespace ActualChat.Users.Db;

public class DbUserIdHandler : DbUserIdHandler<string>
{
    public DbUserIdHandler(IConverterProvider converters)
        : base(converters, null)
        => Generator = () => UserId.New();
}
