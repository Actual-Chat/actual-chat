using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Fusion.EntityFramework.Operations;

namespace ActualChat.Contacts.Db;

public class ContactsDbContext(DbContextOptions<ContactsDbContext> options) : DbContextBase(options)
{
    public DbSet<DbContact> Contacts { get; protected set; } = null!;
    public DbSet<DbExternalContact> ExternalContacts { get; protected set; } = null!;
    public DbSet<DbExternalContactsHash> ExternalContactsHashes { get; protected set; } = null!;
    public DbSet<DbExternalContactLink> ExternalContactLinks { get; protected set; } = null!;
    public DbSet<DbPlaceContact> PlaceContacts { get; protected set; } = null!;

    // ActualLab.Fusion.EntityFramework tables
    public DbSet<DbOperation> Operations { get; protected set; } = null!;
    public DbSet<DbEvent> Events { get; protected set; } = null!;

    protected override void OnModelCreating(ModelBuilder model)
        => model.ApplyConfigurationsFromAssembly(typeof(ContactsDbContext).Assembly).UseSnakeCaseNaming();
}
