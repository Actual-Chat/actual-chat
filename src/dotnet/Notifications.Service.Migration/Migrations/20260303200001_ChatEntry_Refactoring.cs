using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Notifications.Migrations
{
    /// <inheritdoc />
    public partial class ChatEntry_Refactoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "text_entry_local_id",
                table: "notifications",
                newName: "chat_entry_lid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "chat_entry_lid",
                table: "notifications",
                newName: "text_entry_local_id");
        }
    }
}
