namespace ActualChat.Users;

public static class SystemPropertiesExt
{
    public static Task<ServerApiInfo> GetServerApiInfo(
        this ISystemProperties systemProperties,
        CancellationToken cancellationToken)
        => systemProperties.GetServerApiInfo(ApiConstants.VersionString, cancellationToken);

}
