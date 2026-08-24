namespace ActualChat.Users.Phone.Internal;

public static class Errors
{
    private const string DeliveryFailureMessage = "We couldn't deliver the message to the specified phone number.";

    public static Exception DeliveryFailed()
        => StandardError.External(DeliveryFailureMessage);

    public static Exception DeliveryFailed(Exception innerException)
        => StandardError.External(DeliveryFailureMessage, innerException);
}
