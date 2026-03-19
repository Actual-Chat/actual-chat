namespace ActualChat;

public sealed record PhoneCode(string Country, string DisplayCode)
{
    public string Code => Phone.NormalizePart(DisplayCode);
}
