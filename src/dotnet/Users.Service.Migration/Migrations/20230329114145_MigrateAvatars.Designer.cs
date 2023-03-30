﻿// <auto-generated />
using System;
using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ActualChat.Users.Migrations
{
    [DbContext(typeof(UsersDbContext))]
    [Migration("20230329114145_MigrateAvatars")]
    partial class MigrateAvatars
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "7.0.4")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("ActualChat.Users.Db.DbAccount", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id");

                    b.Property<string>("Email")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("email");

                    b.Property<string>("LastName")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("last_name");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("name");

                    b.Property<string>("Phone")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("phone");

                    b.Property<short>("Status")
                        .HasColumnType("smallint")
                        .HasColumnName("status");

                    b.Property<bool>("SyncContacts")
                        .HasColumnType("boolean")
                        .HasColumnName("sync_contacts");

                    b.Property<string>("Username")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("username");

                    b.Property<string>("UsernameNormalized")
                        .HasColumnType("text")
                        .HasColumnName("username_normalized");

                    b.Property<long>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.HasKey("Id")
                        .HasName("pk_accounts");

                    b.HasIndex("UsernameNormalized")
                        .IsUnique()
                        .HasDatabaseName("ix_accounts_username_normalized")
                        .HasFilter("username_normalized is not null");

                    b.ToTable("accounts");
                });

            modelBuilder.Entity("ActualChat.Users.Db.DbAvatar", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id");

                    b.Property<string>("Bio")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("bio");

                    b.Property<string>("MediaId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("media_id");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("name");

                    b.Property<string>("Picture")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("picture");

                    b.Property<string>("UserId")
                        .HasColumnType("text")
                        .HasColumnName("user_id");

                    b.Property<long>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.HasKey("Id")
                        .HasName("pk_avatars");

                    b.ToTable("avatars");
                });

            modelBuilder.Entity("ActualChat.Users.Db.DbChatPosition", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id");

                    b.Property<long>("EntryLid")
                        .HasColumnType("bigint")
                        .HasColumnName("entry_lid");

                    b.Property<int>("Kind")
                        .HasColumnType("integer")
                        .HasColumnName("kind");

                    b.Property<string>("Origin")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("origin");

                    b.HasKey("Id")
                        .HasName("pk_chat_positions");

                    b.ToTable("chat_positions");
                });

            modelBuilder.Entity("ActualChat.Users.Db.DbKvasEntry", b =>
                {
                    b.Property<string>("Key")
                        .HasColumnType("text")
                        .HasColumnName("key");

                    b.Property<string>("Value")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("value");

                    b.Property<long>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.HasKey("Key")
                        .HasName("pk_kvas_entries");

                    b.ToTable("kvas_entries");
                });

            modelBuilder.Entity("ActualChat.Users.Db.DbSessionInfo", b =>
                {
                    b.Property<string>("Id")
                        .HasMaxLength(256)
                        .HasColumnType("character varying(256)")
                        .HasColumnName("id");

                    b.Property<string>("AuthenticatedIdentity")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("authenticated_identity");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<string>("IPAddress")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("ipaddress");

                    b.Property<bool>("IsSignOutForced")
                        .HasColumnType("boolean")
                        .HasColumnName("is_sign_out_forced");

                    b.Property<DateTime>("LastSeenAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("last_seen_at");

                    b.Property<string>("OptionsJson")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("options_json");

                    b.Property<string>("UserAgent")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("user_agent");

                    b.Property<string>("UserId")
                        .HasColumnType("text")
                        .HasColumnName("user_id");

                    b.Property<long>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.HasKey("Id")
                        .HasName("pk_sessions");

                    b.HasIndex("CreatedAt", "IsSignOutForced")
                        .HasDatabaseName("ix_sessions_created_at_is_sign_out_forced");

                    b.HasIndex("IPAddress", "IsSignOutForced")
                        .HasDatabaseName("ix_sessions_ipaddress_is_sign_out_forced");

                    b.HasIndex("LastSeenAt", "IsSignOutForced")
                        .HasDatabaseName("ix_sessions_last_seen_at_is_sign_out_forced");

                    b.HasIndex("UserId", "IsSignOutForced")
                        .HasDatabaseName("ix_sessions_user_id_is_sign_out_forced");

                    b.ToTable("_sessions");
                });

            modelBuilder.Entity("ActualChat.Users.Db.DbUser", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id");

                    b.Property<string>("ClaimsJson")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("claims_json");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("name");

                    b.Property<long>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.HasKey("Id")
                        .HasName("pk_users");

                    b.HasIndex("Name")
                        .HasDatabaseName("ix_users_name");

                    b.ToTable("users");
                });

            modelBuilder.Entity("ActualChat.Users.Db.DbUserPresence", b =>
                {
                    b.Property<string>("UserId")
                        .HasColumnType("text")
                        .HasColumnName("user_id");

                    b.Property<DateTime>("OnlineCheckInAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("online_check_in_at");

                    b.HasKey("UserId")
                        .HasName("pk_presences");

                    b.ToTable("presences");
                });

            modelBuilder.Entity("Stl.Fusion.EntityFramework.Authentication.DbUserIdentity<string>", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id");

                    b.Property<string>("DbUserId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("user_id");

                    b.Property<string>("Secret")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("secret");

                    b.HasKey("Id")
                        .HasName("pk_user_identities");

                    b.HasIndex("DbUserId")
                        .HasDatabaseName("ix_user_identities_user_id");

                    b.HasIndex("Id")
                        .HasDatabaseName("ix_user_identities_id");

                    b.ToTable("user_identities");
                });

            modelBuilder.Entity("Stl.Fusion.EntityFramework.Operations.DbOperation", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id");

                    b.Property<string>("AgentId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("agent_id");

                    b.Property<string>("CommandJson")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("command_json");

                    b.Property<DateTime>("CommitTime")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("commit_time");

                    b.Property<string>("ItemsJson")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("items_json");

                    b.Property<DateTime>("StartTime")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("start_time");

                    b.HasKey("Id")
                        .HasName("pk_operations");

                    b.HasIndex(new[] { "CommitTime" }, "IX_CommitTime")
                        .HasDatabaseName("ix_commit_time");

                    b.HasIndex(new[] { "StartTime" }, "IX_StartTime")
                        .HasDatabaseName("ix_start_time");

                    b.ToTable("_operations");
                });

            modelBuilder.Entity("Stl.Fusion.EntityFramework.Authentication.DbUserIdentity<string>", b =>
                {
                    b.HasOne("ActualChat.Users.Db.DbUser", null)
                        .WithMany("Identities")
                        .HasForeignKey("DbUserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired()
                        .HasConstraintName("fk_user_identities_users_user_id");
                });

            modelBuilder.Entity("ActualChat.Users.Db.DbUser", b =>
                {
                    b.Navigation("Identities");
                });
#pragma warning restore 612, 618
        }
    }
}
