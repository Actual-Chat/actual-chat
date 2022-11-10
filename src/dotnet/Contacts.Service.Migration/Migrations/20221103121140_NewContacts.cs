using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Contacts.Migrations
{
    public partial class NewContacts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "_operations",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    agent_id = table.Column<string>(type: "text", nullable: false),
                    start_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    commit_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    command_json = table.Column<string>(type: "text", nullable: false),
                    items_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "contacts",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    owner_id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: true),
                    chat_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contacts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_commit_time",
                table: "_operations",
                column: "commit_time");

            migrationBuilder.CreateIndex(
                name: "ix_start_time",
                table: "_operations",
                column: "start_time");

            migrationBuilder.CreateIndex(
                name: "ix_contacts_owner_id",
                table: "contacts",
                column: "owner_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "_operations");

            migrationBuilder.DropTable(
                name: "contacts");
        }
    }
}
