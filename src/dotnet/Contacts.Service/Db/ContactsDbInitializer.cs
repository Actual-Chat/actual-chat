using ActualChat.Db;

namespace ActualChat.Contacts.Db;

public class ContactsDbInitializer : DbInitializer<ContactsDbContext>
{
    public ContactsDbInitializer(IServiceProvider services) : base(services)
    { }
}
