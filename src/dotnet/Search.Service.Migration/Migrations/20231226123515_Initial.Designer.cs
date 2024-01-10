﻿// <auto-generated />
using System;
using ActualChat.Search.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ActualChat.Search.Migrations
{
    [DbContext(typeof(SearchDbContext))]
    [Migration("20231226123515_Initial")]
    partial class Initial
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.0")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("ActualChat.Search.Db.DbIndexedChat", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id");

                    b.Property<DateTime>("ChatCreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("chat_created_at");

                    b.Property<long>("LastEntryLocalId")
                        .HasColumnType("bigint")
                        .HasColumnName("last_entry_local_id");

                    b.Property<long>("LastEntryVersion")
                        .HasColumnType("bigint")
                        .HasColumnName("last_entry_version");

                    b.Property<long>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.HasKey("Id")
                        .HasName("pk_indexed_chat");

                    b.HasIndex("ChatCreatedAt")
                        .HasDatabaseName("ix_indexed_chat_chat_created_at");

                    b.ToTable("indexed_chat");
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
