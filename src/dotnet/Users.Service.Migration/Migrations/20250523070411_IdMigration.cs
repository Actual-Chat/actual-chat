using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    /// <inheritdoc />
    public partial class IdMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_accounts_alias_id",
                table: "accounts");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_alias_id",
                table: "accounts",
                column: "alias_id",
                unique: true,
                filter: "alias_id <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_accounts_alias_id",
                table: "accounts");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_alias_id",
                table: "accounts",
                column: "alias_id",
                unique: true,
                filter: "user_link_id <> ''");
        }
    }
}
