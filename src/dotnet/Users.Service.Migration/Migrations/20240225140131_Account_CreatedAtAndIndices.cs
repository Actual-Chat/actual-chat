using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    /// <inheritdoc />
    public partial class Account_CreatedAtAndIndices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "accounts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateIndex(
                name: "ix_accounts_created_at",
                table: "accounts",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_version_id",
                table: "accounts",
                columns: new[] { "version", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_accounts_created_at",
                table: "accounts");

            migrationBuilder.DropIndex(
                name: "ix_accounts_version_id",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "accounts");
        }
    }
}
