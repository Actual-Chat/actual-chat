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
    {
        model.ApplyConfigurationsFromAssembly(typeof(ContactsDbContext).Assembly).UseSnakeCaseNaming();

        var contact = model.Entity<DbContact>();
        contact.Property(e => e.Id).UseCollation("C");
        contact.Property(e => e.OwnerId).UseCollation("C");
        contact.Property(e => e.UserId).UseCollation("C");
        contact.Property(c => c.ChatId).UseCollation("C");
        contact.Property(c => c.PlaceId).UseCollation("C");

        var externalContact = model.Entity<DbExternalContact>();
        externalContact.Property(e => e.Id).UseCollation("C");

        var externalContactHash = model.Entity<DbExternalContactsHash>();
        externalContactHash.Property(e => e.Id).UseCollation("C");

        var externalContactLink = model.Entity<DbExternalContactLink>();
        externalContactLink.Property(e => e.DbExternalContactId).UseCollation("C");

        var placeContact = model.Entity<DbPlaceContact>();
        placeContact.Property(e => e.Id).UseCollation("C");
        placeContact.Property(e => e.PlaceId).UseCollation("C");
        placeContact.Property(e => e.OwnerId).UseCollation("C");

        var operation = model.Entity<DbOperation>();
        operation.Property(e => e.Uuid).UseCollation("C");
        operation.Property(e => e.HostId).UseCollation("C");

        var events = model.Entity<DbEvent>();
        events.Property(e => e.Uuid).UseCollation("C");
    }
}
