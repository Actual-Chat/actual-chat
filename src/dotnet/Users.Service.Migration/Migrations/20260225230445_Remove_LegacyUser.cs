using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    /// <inheritdoc />
    public partial class Remove_LegacyUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "user_identities",
                newName: "_gc_user_identities");

            migrationBuilder.RenameTable(
                name: "users",
                newName: "_gc_users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "_gc_users",
                newName: "users");

            migrationBuilder.RenameTable(
                name: "_gc_user_identities",
                newName: "user_identities");
        }
    }
}
