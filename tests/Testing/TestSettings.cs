namespace ActualChat.Testing;

public record TestSettings
{
    public string TempDirectory { get; set; } = "";
    public bool IsRunningInContainer { get; set; } = false;

    public string RedisConfiguration { get; set; } = "";
}
