﻿// <auto-generated />
using System;
using ActualChat.Media.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ActualChat.Media.Migrations
{
    [DbContext(typeof(MediaDbContext))]
    partial class MediaDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.4")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("ActualChat.Media.Db.DbLinkPreview", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<string>("Description")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("description");

                    b.Property<string>("MetadataJson")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("metadata_json");

                    b.Property<DateTime>("ModifiedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("modified_at");

                    b.Property<string>("ThumbnailMediaId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("thumbnail_media_id");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("title");

                    b.Property<string>("Url")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("url");

                    b.Property<long>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.HasKey("Id")
                        .HasName("pk_link_previews");

                    b.ToTable("link_previews");
                });

            modelBuilder.Entity("ActualChat.Media.Db.DbMedia", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id");

                    b.Property<string>("ContentId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("content_id");

                    b.Property<string>("LocalId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("local_id");

                    b.Property<string>("MetadataJson")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("metadata_json");

                    b.Property<string>("Scope")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("scope");

                    b.HasKey("Id")
                        .HasName("pk_media");

                    b.HasIndex("ContentId")
                        .HasDatabaseName("ix_media_content_id");

                    b.ToTable("media");
                });

            modelBuilder.Entity("ActualLab.Fusion.EntityFramework.Operations.DbEvent", b =>
                {
                    b.Property<string>("Uuid")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("text")
                        .HasColumnName("uuid");

                    b.Property<DateTime>("DelayUntil")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("delay_until");

                    b.Property<DateTime>("LoggedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("logged_at");

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
                        .HasName("pk_events");

                    b.HasIndex("DelayUntil")
                        .HasDatabaseName("ix_events_delay_until");

                    b.HasIndex("Uuid")
                        .IsUnique()
                        .HasDatabaseName("ix_events_uuid");

                    b.HasIndex("State", "DelayUntil")
                        .HasDatabaseName("ix_events_state_delay_until");

                    b.ToTable("_events");
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
#pragma warning restore 612, 618
        }
    }
}
