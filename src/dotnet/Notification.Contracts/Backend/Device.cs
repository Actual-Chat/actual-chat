namespace ActualChat.Notification.Backend;

public record Device(string DeviceId, DeviceType DeviceType, Moment CreatedAt, Moment? AccessedAt);
