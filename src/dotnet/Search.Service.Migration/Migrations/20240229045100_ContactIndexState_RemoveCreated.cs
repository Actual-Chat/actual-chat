using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Search.Migrations
{
    /// <inheritdoc />
    public partial class ContactIndexState_RemoveCreated : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_created_at",
                table: "contact_index_state");

            migrationBuilder.DropColumn(
                name: "last_created_id",
                table: "contact_index_state");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "last_created_at",
                table: "contact_index_state",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "last_created_id",
                table: "contact_index_state",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
