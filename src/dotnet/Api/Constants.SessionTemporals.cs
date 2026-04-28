namespace ActualChat;

public static partial class Constants
{
    public static class SessionTemporals
    {
        public const string SignInErrorKey = "SignInError";
        public const string PendingRegistrationKey = "PendingRegistration";
        // Written to SignInErrorKey when the user cancels a registration
        // confirmation prompt. The UI uses this exact string to detect
        // a cancel and reset the sign-in form.
        public const string SignInCanceledMessage = "Sign-in canceled.";
    }
}
