using ActualLab.Conversion;
using ActualLab.Fusion.Authentication.Services;

namespace ActualChat.Users.Db;

[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbUserIdHandler(IConverterProvider converters)
    : DbUserIdHandler<string>(converters, () => UserId.New().Value);
