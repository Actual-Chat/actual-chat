namespace ActualChat.Nats.Module;

public sealed class NatsSettings
{
    public string Url { get; set; } = "nats://localhost:4222";
    public string NKey { get; set; } = ""; // e.g "UDXU4RCSJNZOIQHZNWXHXORDPRTGNJAHAHFRGZNEEJCPQTT2M7NLCNF4";
    public string Seed { get; set; } = ""; // e.g. "SUACSSL3UAHUDXKFSNVUZRF5UHPMWZ6BFDTJ7M6USDXIEDNPPQYYYCU3VY";
}
