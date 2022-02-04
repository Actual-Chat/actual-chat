namespace ActualChat.Db;

public static class ServiceProviderExtensions
{
    public static LocalIdGeneratorFactory LocalIdGeneratorFactory(this IServiceProvider svp)
        => new LocalIdGeneratorFactory(svp);
}
