using ActualChat.Contacts.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ActualChat.Contacts;

public class ContactsDbContextContextFactory : IDesignTimeDbContextFactory<ContactsDbContext>
{
    public string ConnectionString =
        "Server=localhost;Database=ac_dev_contacts;Port=5432;User Id=postgres;Password=postgres;Include Error Detail=True";

    public ContactsDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<ContactsDbContext>();
        builder.UseNpgsql(
            ConnectionString,
            o => o.MigrationsAssembly(typeof(ContactsDbContextContextFactory).Assembly.FullName));

        return new ContactsDbContext(builder.Options);
    }
}
