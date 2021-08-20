﻿using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework.Operations;

namespace ActualChat.Chat.Db
{
    public class ChatDbContext : DbContext
    {
        public DbSet<DbChat> Chats { get; protected set; } = null!;
        public DbSet<DbChatEntry> ChatEntries { get; protected set; } = null!;
        public DbSet<DbChatOwner> ChatOwners { get; protected set; } = null!;

        // Stl.Fusion.EntityFramework tables
        public DbSet<DbOperation> Operations { get; protected set; } = null!;

        public ChatDbContext(DbContextOptions options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder model)
        {
            // DbChatEntry
            model.Entity<DbChatEntry>()
                .HasKey(e => new {e.ChatId, e.Id});

            // DbChatOwner
            model.Entity<DbChatOwner>()
                .HasKey(e => new { e.ChatId, e.UserId});
        }
    }
}
