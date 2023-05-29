namespace ActualChat.Users.Models;

internal class AppleUser
{
    public AppleUserName? Name { get; init; }
    public string? Email { get; init; }
}

internal class AppleUserName
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
}
