using ActualChat.Db;

namespace ActualChat.Contacts.Db;

public class ContactsDbInitializer(IServiceProvider services) : DbInitializer<ContactsDbContext>(services);
