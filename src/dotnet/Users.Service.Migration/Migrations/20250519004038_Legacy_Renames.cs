using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    /// <inheritdoc />
    public partial class Legacy_Renames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "country_code",
                table: "roulette_profile_prefs",
                newName: "country");

            migrationBuilder.RenameColumn(
                name: "user_link_id",
                table: "accounts",
                newName: "alias_id");

            migrationBuilder.RenameIndex(
                name: "ix_accounts_user_link_id",
                table: "accounts",
                newName: "ix_accounts_alias_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "country",
                table: "roulette_profile_prefs",
                newName: "country_code");

            migrationBuilder.RenameColumn(
                name: "alias_id",
                table: "accounts",
                newName: "user_link_id");

            migrationBuilder.RenameIndex(
                name: "ix_accounts_alias_id",
                table: "accounts",
                newName: "ix_accounts_user_link_id");
        }
    }
}
