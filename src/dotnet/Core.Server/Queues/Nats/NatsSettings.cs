﻿namespace ActualChat.Queues.Nats;

public sealed class NatsSettings
{
    public string Url { get; set; } = "nats://localhost:4222";
    public string NKey { get; set; } = ""; // e.g "UDXU4RCSJNZOIQHZNWXHXORDPRTGNJAHAHFRGZNEEJCPQTT2M7NLCNF4";
    public string Seed { get; set; } = ""; // e.g. "SUACSSL3UAHUDXKFSNVUZRF5UHPMWZ6BFDTJ7M6USDXIEDNPPQYYYCU3VY";

    // The value of this property is derived from CoreSettings.Instance rather than read from the configuration.
    public string InstancePrefix { get; set; } = "";
}
