﻿// <auto-generated />
using System;
using ActualChat.Db;
using ActualChat.Notification.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ActualChat.Notification.Migrations
{
    [DbContext(typeof(NotificationDbContext))]
    [Migration("20240411093333_Fusion75")]
    partial class Fusion75
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.4")
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

            modelBuilder.Entity("ActualChat.Notification.Db.DbNotification", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id");

                    b.Property<string>("AuthorId")
                        .HasColumnType("text")
                        .HasColumnName("author_id");

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

                    b.Property<string>("IconUrl")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("icon_url");

                    b.Property<int>("Kind")
                        .HasColumnType("integer")
                        .HasColumnName("kind");

                    b.Property<DateTime>("SentAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("sent_at");

                    b.Property<string>("SimilarityKey")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("similarity_key");

                    b.Property<long?>("TextEntryLocalId")
                        .HasColumnType("bigint")
                        .HasColumnName("text_entry_local_id");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("title");

                    b.Property<string>("UserId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("user_id");

                    b.Property<long>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.HasKey("Id")
                        .HasName("pk_notifications");

                    b.HasIndex("UserId", "Id")
                        .HasDatabaseName("ix_notifications_user_id_id");

                    b.HasIndex("UserId", "Version")
                        .HasDatabaseName("ix_notifications_user_id_version");

                    b.HasIndex("UserId", "Kind", "SimilarityKey")
                        .HasDatabaseName("ix_notifications_user_id_kind_similarity_key");

                    b.ToTable("notifications");

                    b.HasAnnotation("ConflictStrategy", ConflictStrategy.DoNothing);
                });

            modelBuilder.Entity("ActualLab.Fusion.EntityFramework.Operations.DbOperation", b =>
                {
                    b.Property<long>("Index")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasColumnName("index");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<long>("Index"));

                    b.Property<string>("CommandJson")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("command_json");

                    b.Property<string>("HostId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("host_id");

                    b.Property<string>("ItemsJson")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("items_json");

                    b.Property<DateTime>("LoggedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("logged_at");

                    b.Property<string>("NestedOperations")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("nested_operations");

                    b.Property<string>("Uuid")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("uuid");

                    b.HasKey("Index")
                        .HasName("pk_operations");

                    b.HasIndex("LoggedAt")
                        .HasDatabaseName("ix_operations_logged_at");

                    b.HasIndex("Uuid")
                        .IsUnique()
                        .HasDatabaseName("ix_operations_uuid");

                    b.ToTable("_operations");
                });

            modelBuilder.Entity("ActualLab.Fusion.EntityFramework.Operations.DbOperationEvent", b =>
                {
                    b.Property<long>("Index")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasColumnName("index");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<long>("Index"));

                    b.Property<DateTime>("LoggedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("logged_at");

                    b.Property<int>("State")
                        .HasColumnType("integer")
                        .HasColumnName("state");

                    b.Property<string>("Uuid")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("uuid");

                    b.Property<string>("ValueJson")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("value_json");

                    b.Property<long>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.HasKey("Index")
                        .HasName("pk_events");

                    b.HasIndex("LoggedAt")
                        .HasDatabaseName("ix_events_logged_at");

                    b.HasIndex("Uuid")
                        .IsUnique()
                        .HasDatabaseName("ix_events_uuid");

                    b.HasIndex("State", "LoggedAt")
                        .HasDatabaseName("ix_events_state_logged_at");

                    b.ToTable("_events");
                });

            modelBuilder.Entity("ActualLab.Fusion.EntityFramework.Operations.DbOperationTimer", b =>
                {
                    b.Property<string>("Uuid")
                        .HasColumnType("text")
                        .HasColumnName("uuid");

                    b.Property<DateTime>("FiresAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("fires_at");

                    b.Property<int>("State")
                        .HasColumnType("integer")
                        .HasColumnName("state");

                    b.Property<string>("ValueJson")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("value_json");

                    b.Property<long>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.HasKey("Uuid")
                        .HasName("pk_timers");

                    b.HasIndex("FiresAt")
                        .HasDatabaseName("ix_timers_fires_at");

                    b.HasIndex("State", "FiresAt")
                        .HasDatabaseName("ix_timers_state_fires_at");

                    b.ToTable("_timers");
                });
#pragma warning restore 612, 618
        }
    }
}
