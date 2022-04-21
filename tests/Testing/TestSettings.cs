namespace ActualChat.Testing;

public record TestSettings
{
    public string TempDirectory { get; set; } = "";
    public bool IsRunningInContainer { get; set; } = false;
}
