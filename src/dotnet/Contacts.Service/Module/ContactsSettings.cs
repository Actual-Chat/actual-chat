namespace ActualChat.Contacts.Module;

public sealed class ContactsSettings
{
    // DBs
    public string Db { get; set; } = "";
    public string Redis { get; set; } = "";
    public TimeSpan GreetingTimeout { get; set; } = TimeSpan.FromSeconds(20);
}
