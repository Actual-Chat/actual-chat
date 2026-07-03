namespace ActualChat.Hosting;

/// <summary>
/// Device thermal pressure. Maps 1:1 to iOS <c>NSProcessInfoThermalState</c>,
/// Android <c>PowerManager</c> THERMAL_STATUS bands, and the web Compute
/// Pressure API states (nominal/fair/serious/critical).
/// </summary>
public enum ThermalLevel
{
    Nominal = 0,
    Fair = 1,
    Serious = 2,
    Critical = 3,
}
