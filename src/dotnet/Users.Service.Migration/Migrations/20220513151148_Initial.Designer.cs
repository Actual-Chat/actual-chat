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
    [Migration("20220513151148_Initial")]
    partial class Initial
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "6.0.5")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("ActualChat.Users.Db.DbChatReadPosition", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id");

                    b.Property<long>("ReadEntryId")
                        .HasColumnType("bigint")
                        .HasColumnName("read_entry_id");

                    b.HasKey("Id")
                        .HasName("pk_chat_read_positions");

                    b.ToTable("chat_read_positions");
                });

            modelBuilder.Entity("ActualChat.Users.Db.DbChatUserSettings", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id");

                    b.Property<string>("AvatarId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("avatar_id");

                    b.Property<string>("ChatId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("chat_id");

                    b.Property<string>("Language")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("language");

                    b.Property<string>("UserId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("user_id");

                    b.Property<long>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.HasKey("Id")
                        .HasName("pk_chat_user_settings");

                    b.HasIndex("ChatId", "UserId")
                        .HasDatabaseName("ix_chat_user_settings_chat_id_user_id");

                    b.ToTable("chat_user_settings");
                });

            modelBuilder.Entity("ActualChat.Users.Db.DbSessionInfo", b =>
                {
                    b.Property<string>("Id")
                        .HasMaxLength(32)
                        .HasColumnType("character varying(32)")
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
                        .IsRequired()
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

            modelBuilder.Entity("ActualChat.Users.Db.DbUserAvatar", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id");

                    b.Property<string>("Bio")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("bio");

                    b.Property<long>("LocalId")
                        .HasColumnType("bigint")
                        .HasColumnName("local_id");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("name");

                    b.Property<string>("Picture")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("picture");

                    b.Property<string>("UserId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("user_id");

                    b.Property<long>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.HasKey("Id")
                        .HasName("pk_user_avatars");

                    b.ToTable("user_avatars");
                });

            modelBuilder.Entity("ActualChat.Users.Db.DbUserContact", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("name");

                    b.Property<string>("OwnerUserId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("owner_user_id");

                    b.Property<string>("TargetUserId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("target_user_id");

                    b.Property<long>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.HasKey("Id")
                        .HasName("pk_user_contacts");

                    b.HasIndex("OwnerUserId")
                        .HasDatabaseName("ix_user_contacts_owner_user_id");

                    b.ToTable("user_contacts");
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
                        .HasName("pk_user_presences");

                    b.ToTable("user_presences");
                });

            modelBuilder.Entity("ActualChat.Users.Db.DbUserProfile", b =>
                {
                    b.Property<string>("UserId")
                        .HasColumnType("text")
                        .HasColumnName("user_id");

                    b.Property<string>("AvatarId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("avatar_id");

                    b.Property<short>("Status")
                        .HasColumnType("smallint")
                        .HasColumnName("status");

                    b.Property<long>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.HasKey("UserId")
                        .HasName("pk_user_profiles");

                    b.ToTable("user_profiles");
                });

            modelBuilder.Entity("ActualLab.Fusion.EntityFramework.Authentication.DbUserIdentity<string>", b =>
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

            modelBuilder.Entity("ActualLab.Fusion.EntityFramework.Operations.DbOperation", b =>
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

            modelBuilder.Entity("ActualLab.Fusion.EntityFramework.Authentication.DbUserIdentity<string>", b =>
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
