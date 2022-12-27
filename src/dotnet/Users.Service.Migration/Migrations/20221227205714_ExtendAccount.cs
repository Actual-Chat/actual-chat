using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    /// <inheritdoc />
    public partial class ExtendAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "email",
                table: "accounts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "last_name",
                table: "accounts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "name",
                table: "accounts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "phone",
                table: "accounts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "sync_contacts",
                table: "accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "username",
                table: "accounts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "username_normalized",
                table: "accounts",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_accounts_username_normalized",
                table: "accounts",
                column: "username_normalized",
                unique: true,
                filter: "username_normalized is not null");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_accounts_username_normalized",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "email",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "last_name",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "name",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "phone",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "sync_contacts",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "username",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "username_normalized",
                table: "accounts");
        }
    }
}
