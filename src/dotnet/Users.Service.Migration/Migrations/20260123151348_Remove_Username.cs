using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    /// <inheritdoc />
    public partial class Remove_Username : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_accounts_username_normalized",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "username",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "username_normalized",
                table: "accounts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
    }
}
