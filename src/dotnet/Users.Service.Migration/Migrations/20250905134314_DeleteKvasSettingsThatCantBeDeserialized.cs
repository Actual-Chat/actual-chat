using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    /// <inheritdoc />
    public partial class DeleteKvasSettingsThatCantBeDeserialized : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM kvas_entries
                WHERE key LIKE '%UserListeningSettings';
                """);
            migrationBuilder.Sql("""
                DELETE FROM kvas_entries
                WHERE key LIKE '%UserNavbarSettings';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: cannot restore deleted key-value entries.
        }
    }
}
