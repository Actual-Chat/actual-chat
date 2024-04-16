﻿// <auto-generated />
using System;
using ActualChat.MLSearch.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ActualChat.MLSearch.Migrations
{
    [DbContext(typeof(MLSearchDbContext))]
    partial class MLSearchDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.4")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

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
