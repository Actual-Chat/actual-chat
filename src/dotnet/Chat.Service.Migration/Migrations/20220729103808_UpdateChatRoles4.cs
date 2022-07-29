using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    public partial class UpdateChatRoles4 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_chat_author_roles_chat_authors_db_chat_author_id",
                table: "chat_author_roles");

            migrationBuilder.DropIndex(
                name: "ix_chat_author_roles_db_chat_author_id",
                table: "chat_author_roles");

            migrationBuilder.DropColumn(
                name: "db_chat_author_id",
                table: "chat_author_roles");

            migrationBuilder.AddForeignKey(
                name: "fk_chat_author_roles_chat_authors_chat_author_id",
                table: "chat_author_roles",
                column: "chat_author_id",
                principalTable: "chat_authors",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_chat_author_roles_chat_authors_chat_author_id",
                table: "chat_author_roles");

            migrationBuilder.AddColumn<string>(
                name: "db_chat_author_id",
                table: "chat_author_roles",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_chat_author_roles_db_chat_author_id",
                table: "chat_author_roles",
                column: "db_chat_author_id");

            migrationBuilder.AddForeignKey(
                name: "fk_chat_author_roles_chat_authors_db_chat_author_id",
                table: "chat_author_roles",
                column: "db_chat_author_id",
                principalTable: "chat_authors",
                principalColumn: "id");
        }
    }
}
