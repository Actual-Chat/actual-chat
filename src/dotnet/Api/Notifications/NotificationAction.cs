namespace ActualChat.Notifications;

/// <summary>
/// A call-to-action the client can render as a button on a notification. Only kinds the client can
/// honor on its own are defined here; richer kinds (Accept/Decline etc.) arrive with their server
/// producers + command routing.
/// </summary>
public enum NotificationActionKind
{
    None = 0,
    Open,
    Dismiss,
}

/// <summary>
/// One action button on a <see cref="Notification"/>. <see cref="Target"/> is an optional local URL
/// for <see cref="NotificationActionKind.Open"/> (falls back to the notification's own link).
/// </summary>
[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record NotificationAction(
    [property: DataMember(Order = 0), Key(0)] NotificationActionKind Kind,
    [property: DataMember(Order = 1), Key(1)] string Title,
    [property: DataMember(Order = 2), Key(2)] string Target = "");
