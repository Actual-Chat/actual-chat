using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Operations;

namespace ActualChat.Contacts.Db;

public class ContactsDbContext : DbContextBase
{
    public DbSet<DbContact> Contacts { get; protected set; } = null!;
    public DbSet<DbExternalContact> ExternalContacts { get; protected set; } = null!;
    public DbSet<DbExternalContactLink> ExternalContactLinks { get; protected set; } = null!;

    // Stl.Fusion.EntityFramework tables
    public DbSet<DbOperation> Operations { get; protected set; } = null!;

    public ContactsDbContext(DbContextOptions<ContactsDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder model)
        => model.ApplyConfigurationsFromAssembly(typeof(ContactsDbContext).Assembly).UseSnakeCaseNaming();
}
