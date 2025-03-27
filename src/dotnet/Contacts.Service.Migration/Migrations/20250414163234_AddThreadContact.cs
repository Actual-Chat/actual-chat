using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Contacts.Migrations
{
    /// <inheritdoc />
    public partial class AddThreadContact : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "thread_contacts",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    owner_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    thread_chat_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    parent_chat_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    outermost_parent_chat_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    place_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    is_pinned = table.Column<bool>(type: "boolean", nullable: false),
                    touched_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_thread_contacts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_thread_contacts_owner_id",
                table: "thread_contacts",
                column: "owner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "thread_contacts");
        }
    }
}
