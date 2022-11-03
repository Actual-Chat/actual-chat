using ActualChat.Db;

namespace ActualChat.Contacts.Db;

public class ContactsDbInitializer : DbInitializer<ContactsDbContext>
{
    public ContactsDbInitializer(IServiceProvider services) : base(services)
    { }

    public override async Task Initialize(CancellationToken cancellationToken)
    {
        var dependencies = (
            from kv in InitializeTasks
            let dbInitializer = kv.Key
            let dbInitializerName = dbInitializer.GetType().Name
            let task = kv.Value
            where
                OrdinalEquals(dbInitializerName, "UsersDbInitializer")
                || OrdinalEquals(dbInitializerName, "ChatDbInitializer")
            select task
            ).ToArray();
        await Task.WhenAll(dependencies).ConfigureAwait(false);
        await base.Initialize(cancellationToken).ConfigureAwait(false);
    }
}
