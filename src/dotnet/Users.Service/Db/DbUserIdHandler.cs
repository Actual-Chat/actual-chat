using ActualLab.Conversion;

namespace ActualChat.Users.Db;

[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbUserIdHandler(IConverterProvider converters)
    : DbUserIdHandlerBase<string>(converters, () => UserId.New().Value);
