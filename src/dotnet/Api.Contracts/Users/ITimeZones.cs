namespace ActualChat.Users;

public interface ITimeZones : IComputeService
{
    [ComputeMethod]
    Task<ApiArray<TimeZone>> List(string languageCode, CancellationToken cancellationToken);
}
