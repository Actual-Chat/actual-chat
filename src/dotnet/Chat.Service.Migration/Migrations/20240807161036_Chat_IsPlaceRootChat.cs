using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class Chat_IsPlaceRootChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_chats_version_id",
                table: "chats");


            migrationBuilder.AddColumn<bool>(
                name: "is_place_root_chat",
                table: "chats",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_chats_version_id_is_place_root_chat",
                table: "chats",
                columns: new[] { "version", "id", "is_place_root_chat" });

            migrationBuilder.Sql("""
                                 update chats
                                 set is_place_root_chat = true
                                 where id like 's-%-%' and split_part(id, '-', 2) = split_part(id, '-', 3);
                                 """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_chats_version_id_is_place_root_chat",
                table: "chats");

            migrationBuilder.DropColumn(
                name: "is_place_root_chat",
                table: "chats");

            migrationBuilder.CreateIndex(
                name: "ix_chats_version_id",
                table: "chats",
                columns: new[] { "version", "id" });
        }
    }
}
