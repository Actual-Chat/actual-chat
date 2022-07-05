﻿// <auto-generated />
using System;
using ActualChat.Notification.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ActualChat.Notification.Migrations
{
    [DbContext(typeof(NotificationDbContext))]
    partial class NotificationDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "6.0.6")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("ActualChat.Notification.Db.DbDevice", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id");

                    b.Property<DateTime?>("AccessedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("accessed_at");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<int>("Type")
                        .HasColumnType("integer")
                        .HasColumnName("type");

                    b.Property<string>("UserId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("user_id");

                    b.Property<long>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.HasKey("Id")
                        .HasName("pk_devices");

                    b.HasIndex("UserId")
                        .HasDatabaseName("ix_devices_user_id");

                    b.ToTable("devices");
                });

            modelBuilder.Entity("ActualChat.Notification.Db.DbMutedChatSubscription", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id");

                    b.Property<string>("ChatId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("chat_id");

                    b.Property<string>("UserId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("user_id");

                    b.Property<long>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.HasKey("Id")
                        .HasName("pk_muted_chat_subscriptions");

                    b.HasIndex("UserId", "ChatId")
                        .HasDatabaseName("ix_muted_chat_subscriptions_user_id_chat_id");

                    b.ToTable("muted_chat_subscriptions");
                });

            modelBuilder.Entity("ActualChat.Notification.Db.DbNotification", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id");

                    b.Property<string>("ChatAuthorId")
                        .HasColumnType("text")
                        .HasColumnName("chat_author_id");

                    b.Property<long?>("ChatEntryId")
                        .HasColumnType("bigint")
                        .HasColumnName("chat_entry_id");

                    b.Property<string>("ChatId")
                        .HasColumnType("text")
                        .HasColumnName("chat_id");

                    b.Property<string>("Content")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("content");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<DateTime?>("HandledAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("handled_at");

                    b.Property<DateTime?>("ModifiedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("modified_at");

                    b.Property<int>("NotificationType")
                        .HasColumnType("integer")
                        .HasColumnName("notification_type");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("title");

                    b.Property<string>("UserId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("user_id");

                    b.HasKey("Id")
                        .HasName("pk_notifications");

                    b.ToTable("notifications");
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
#pragma warning restore 612, 618
        }
    }
}
