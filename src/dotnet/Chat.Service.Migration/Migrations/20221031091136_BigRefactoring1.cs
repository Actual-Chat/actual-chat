using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    public partial class BigRefactoring1 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_chat_author_roles_chat_authors_chat_author_id",
                table: "chat_author_roles");

            migrationBuilder.DropColumn(
                name: "name",
                table: "chat_authors");

            migrationBuilder.RenameColumn(
                name: "chat_role_id",
                table: "chat_author_roles",
                newName: "role_id");

            migrationBuilder.RenameColumn(
                name: "chat_author_id",
                table: "chat_author_roles",
                newName: "author_id");

            migrationBuilder.RenameIndex(
                name: "ix_chat_author_roles_chat_role_id_chat_author_id",
                table: "chat_author_roles",
                newName: "ix_chat_author_roles_role_id_author_id");

            migrationBuilder.AddColumn<string>(
                name: "avatar_id",
                table: "chat_authors",
                type: "text",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_chat_author_roles_chat_authors_author_id",
                table: "chat_author_roles",
                column: "author_id",
                principalTable: "chat_authors",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_chat_author_roles_chat_authors_author_id",
                table: "chat_author_roles");

            migrationBuilder.DropColumn(
                name: "avatar_id",
                table: "chat_authors");

            migrationBuilder.RenameColumn(
                name: "role_id",
                table: "chat_author_roles",
                newName: "chat_role_id");

            migrationBuilder.RenameColumn(
                name: "author_id",
                table: "chat_author_roles",
                newName: "chat_author_id");

            migrationBuilder.RenameIndex(
                name: "ix_chat_author_roles_role_id_author_id",
                table: "chat_author_roles",
                newName: "ix_chat_author_roles_chat_role_id_chat_author_id");

            migrationBuilder.AddColumn<string>(
                name: "name",
                table: "chat_authors",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddForeignKey(
                name: "fk_chat_author_roles_chat_authors_chat_author_id",
                table: "chat_author_roles",
                column: "chat_author_id",
                principalTable: "chat_authors",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
