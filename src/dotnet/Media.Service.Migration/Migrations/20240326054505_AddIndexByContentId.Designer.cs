﻿// <auto-generated />
using System;
using ActualChat.Media.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ActualChat.Media.Migrations
{
    [DbContext(typeof(MediaDbContext))]
    [Migration("20240326054505_AddIndexByContentId")]
    partial class AddIndexByContentId
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.2")
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
#pragma warning restore 612, 618
        }
    }
}
