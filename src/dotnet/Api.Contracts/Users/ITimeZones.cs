namespace ActualChat.Users;

public interface ITimeZones : IComputeService
{
    // NOTE(AY): Should it really be a compute method? Let's discuss this.
    [ComputeMethod]
    Task<ApiArray<TimeZone>> List(string languageCode, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<string> ConvertWindowsToIana(string windowsTimeZone, CancellationToken cancellationToken);
}
