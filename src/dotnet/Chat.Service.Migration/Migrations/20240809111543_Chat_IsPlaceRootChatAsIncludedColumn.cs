using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class Chat_IsPlaceRootChatAsIncludedColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_chats_version_id_is_place_root_chat",
                table: "chats");

            migrationBuilder.CreateIndex(
                name: "ix_chats_version",
                table: "chats",
                column: "version")
                .Annotation("Npgsql:IndexInclude", new[] { "id", "is_place_root_chat" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_chats_version",
                table: "chats");

            migrationBuilder.CreateIndex(
                name: "ix_chats_version_id_is_place_root_chat",
                table: "chats",
                columns: new[] { "version", "id", "is_place_root_chat" });
        }
    }
}
