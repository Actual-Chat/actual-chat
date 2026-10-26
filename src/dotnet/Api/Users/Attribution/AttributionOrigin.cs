namespace ActualChat.Users;

public enum AttributionOrigin
{
    Unknown = 0,
    // An app URL with campaign parameters: the one the app opened with, or a later one
    Url = 1,
    // An invite link or a user page another user shared: "join:<inviteId>" or "user:<userId>"
    Referral = 2,
    PlayReferrer = 3,
    AppleAdServices = 4,
    // The SignUp usage event of a user who signed up before attributions existed
    LegacySignUp = 5,
}
