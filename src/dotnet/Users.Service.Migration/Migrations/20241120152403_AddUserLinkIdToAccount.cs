using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    /// <inheritdoc />
    public partial class AddUserLinkIdToAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "user_link_id",
                table: "accounts",
                type: "text",
                nullable: false,
                defaultValue: "",
                collation: "C");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_user_link_id",
                table: "accounts",
                column: "user_link_id",
                unique: true,
                filter: "user_link_id <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_accounts_user_link_id",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "user_link_id",
                table: "accounts");
        }
    }
}
