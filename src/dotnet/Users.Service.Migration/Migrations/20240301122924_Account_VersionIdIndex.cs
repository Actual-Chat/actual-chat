using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    /// <inheritdoc />
    public partial class Account_VersionIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_accounts_version",
                table: "accounts");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_version_id",
                table: "accounts",
                columns: new[] { "version", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_accounts_version_id",
                table: "accounts");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_version",
                table: "accounts",
                column: "version");
        }
    }
}
