namespace ActualChat.Db;

public static class ServiceProviderExt
{
    public static DbLocalIdGeneratorFactory LocalIdGeneratorFactory(this IServiceProvider services)
        => new(services);
}
