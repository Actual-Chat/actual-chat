﻿// <auto-generated />
using System;
using ActualChat.Contacts.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ActualChat.Contacts.Migrations
{
    [DbContext(typeof(ContactsDbContext))]
    [Migration("20240411093126_Fusion75_Pre")]
    partial class Fusion75_Pre
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.4")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("ActualChat.Contacts.Db.DbContact", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id");

                    b.Property<string>("ChatId")
                        .HasColumnType("text")
                        .HasColumnName("chat_id");

                    b.Property<bool>("IsPinned")
                        .HasColumnType("boolean")
                        .HasColumnName("is_pinned");

                    b.Property<string>("OwnerId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("owner_id");

                    b.Property<string>("PlaceId")
                        .HasColumnType("text")
                        .HasColumnName("place_id");

                    b.Property<DateTime>("TouchedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("touched_at");

                    b.Property<string>("UserId")
                        .HasColumnType("text")
                        .HasColumnName("user_id");

                    b.Property<long>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.HasKey("Id")
                        .HasName("pk_contacts");

                    b.HasIndex("OwnerId")
                        .HasDatabaseName("ix_contacts_owner_id");

                    b.ToTable("contacts");
                });

            modelBuilder.Entity("ActualChat.Contacts.Db.DbExternalContact", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<string>("DisplayName")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("display_name");

                    b.Property<string>("FamilyName")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("family_name");

                    b.Property<string>("GivenName")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("given_name");

                    b.Property<string>("MiddleName")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("middle_name");

                    b.Property<DateTime>("ModifiedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("modified_at");

                    b.Property<string>("NamePrefix")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("name_prefix");

                    b.Property<string>("NameSuffix")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("name_suffix");

                    b.Property<string>("Sha256Hash")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("sha256_hash");

                    b.Property<long>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.HasKey("Id")
                        .HasName("pk_external_contacts");

                    b.ToTable("external_contacts");
                });

            modelBuilder.Entity("ActualChat.Contacts.Db.DbExternalContactLink", b =>
                {
                    b.Property<string>("DbExternalContactId")
                        .HasColumnType("text")
                        .HasColumnName("external_contact_id");

                    b.Property<string>("Value")
                        .HasColumnType("text")
                        .HasColumnName("value");

                    b.Property<bool>("IsChecked")
                        .HasColumnType("boolean")
                        .HasColumnName("is_checked");

                    b.HasKey("DbExternalContactId", "Value")
                        .HasName("pk_external_contact_links");

                    b.HasIndex("IsChecked")
                        .HasDatabaseName("ix_external_contact_links_is_checked");

                    b.HasIndex("Value")
                        .HasDatabaseName("ix_external_contact_links_value");

                    b.ToTable("external_contact_links");
                });

            modelBuilder.Entity("ActualChat.Contacts.Db.DbExternalContactsHash", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<DateTime>("ModifiedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("modified_at");

                    b.Property<string>("Sha256Hash")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("sha256_hash");

                    b.Property<long>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.HasKey("Id")
                        .HasName("pk_external_contacts_hashes");

                    b.ToTable("external_contacts_hashes");
                });

            modelBuilder.Entity("ActualChat.Contacts.Db.DbPlaceContact", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id");

                    b.Property<string>("OwnerId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("owner_id");

                    b.Property<string>("PlaceId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("place_id");

                    b.Property<long>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.HasKey("Id")
                        .HasName("pk_place_contacts");

                    b.HasIndex("OwnerId")
                        .HasDatabaseName("ix_place_contacts_owner_id");

                    b.ToTable("place_contacts");
                });

            modelBuilder.Entity("ActualChat.Contacts.Db.DbExternalContactLink", b =>
                {
                    b.HasOne("ActualChat.Contacts.Db.DbExternalContact", null)
                        .WithMany("ExternalContactLinks")
                        .HasForeignKey("DbExternalContactId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired()
                        .HasConstraintName("fk_external_contact_links_external_contacts_external_contact_id");
                });

            modelBuilder.Entity("ActualChat.Contacts.Db.DbExternalContact", b =>
                {
                    b.Navigation("ExternalContactLinks");
                });
#pragma warning restore 612, 618
        }
    }
}
