namespace ActualChat.Users.Db;

public sealed class DbUserConverter(IServiceProvider services)
    : DbUserConverterBase<DbUser, string>(services);
