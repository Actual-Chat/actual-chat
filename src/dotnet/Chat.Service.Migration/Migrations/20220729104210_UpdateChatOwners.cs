using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    public partial class UpdateChatOwners : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_chat_owners_chats_db_chat_id",
                table: "chat_owners");

            migrationBuilder.DropIndex(
                name: "ix_chat_owners_db_chat_id",
                table: "chat_owners");

            migrationBuilder.DropColumn(
                name: "db_chat_id",
                table: "chat_owners");

            migrationBuilder.AddForeignKey(
                name: "fk_chat_owners_chats_chat_id",
                table: "chat_owners",
                column: "chat_id",
                principalTable: "chats",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_chat_owners_chats_chat_id",
                table: "chat_owners");

            migrationBuilder.AddColumn<string>(
                name: "db_chat_id",
                table: "chat_owners",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_chat_owners_db_chat_id",
                table: "chat_owners",
                column: "db_chat_id");

            migrationBuilder.AddForeignKey(
                name: "fk_chat_owners_chats_db_chat_id",
                table: "chat_owners",
                column: "db_chat_id",
                principalTable: "chats",
                principalColumn: "id");
        }
    }
}
