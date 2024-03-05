using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class Chat_VersionIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_chats_version",
                table: "chats");

            migrationBuilder.CreateIndex(
                name: "ix_chats_version_id",
                table: "chats",
                columns: new[] { "version", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_chats_version_id",
                table: "chats");

            migrationBuilder.CreateIndex(
                name: "ix_chats_version",
                table: "chats",
                column: "version");
        }
    }
}
