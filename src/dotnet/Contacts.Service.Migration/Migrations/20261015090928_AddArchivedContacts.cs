using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Contacts.Migrations;

/// <inheritdoc />
public partial class _20261015090928_AddArchivedContacts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "archived_contacts",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                version = table.Column<long>(type: "bigint", nullable: false),
                owner_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                user_id = table.Column<string>(type: "text", nullable: true, collation: "C"),
                chat_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                place_id = table.Column<string>(type: "text", nullable: true, collation: "C"),
                peer_contact_name = table.Column<string>(type: "text", nullable: false),
                external_contact_name = table.Column<string>(type: "text", nullable: false),
                state = table.Column<int>(type: "integer", nullable: false),
                is_pinned = table.Column<bool>(type: "boolean", nullable: false),
                is_blocked = table.Column<bool>(type: "boolean", nullable: false),
                touched_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                archived_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_archived_contacts", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_archived_contacts_owner_id_is_blocked",
            table: "archived_contacts",
            columns: new[] { "owner_id", "is_blocked" });

        migrationBuilder.CreateIndex(
            name: "ix_archived_contacts_owner_id_place_id_archived_at",
            table: "archived_contacts",
            columns: new[] { "owner_id", "place_id", "archived_at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "archived_contacts");
    }
}
