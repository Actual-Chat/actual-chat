using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    public partial class BigRefactoring2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_chat_author_roles_chat_authors_author_id",
                table: "chat_author_roles");

            migrationBuilder.DropPrimaryKey(
                name: "pk_chat_roles",
                table: "chat_roles");

            migrationBuilder.DropPrimaryKey(
                name: "pk_chat_authors",
                table: "chat_authors");

            migrationBuilder.DropPrimaryKey(
                name: "pk_chat_author_roles",
                table: "chat_author_roles");

            migrationBuilder.RenameTable(
                name: "chat_roles",
                newName: "roles");

            migrationBuilder.RenameTable(
                name: "chat_authors",
                newName: "authors");

            migrationBuilder.RenameTable(
                name: "chat_author_roles",
                newName: "author_roles");

            migrationBuilder.RenameIndex(
                name: "ix_chat_roles_chat_id_name",
                table: "roles",
                newName: "ix_roles_chat_id_name");

            migrationBuilder.RenameIndex(
                name: "ix_chat_roles_chat_id_local_id",
                table: "roles",
                newName: "ix_roles_chat_id_local_id");

            migrationBuilder.RenameIndex(
                name: "ix_chat_authors_chat_id_user_id",
                table: "authors",
                newName: "ix_authors_chat_id_user_id");

            migrationBuilder.RenameIndex(
                name: "ix_chat_authors_chat_id_local_id",
                table: "authors",
                newName: "ix_authors_chat_id_local_id");

            migrationBuilder.RenameIndex(
                name: "ix_chat_author_roles_role_id_author_id",
                table: "author_roles",
                newName: "ix_author_roles_role_id_author_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_roles",
                table: "roles",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_authors",
                table: "authors",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_author_roles",
                table: "author_roles",
                columns: new[] { "author_id", "role_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_author_roles_authors_author_id",
                table: "author_roles",
                column: "author_id",
                principalTable: "authors",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_author_roles_authors_author_id",
                table: "author_roles");

            migrationBuilder.DropPrimaryKey(
                name: "pk_roles",
                table: "roles");

            migrationBuilder.DropPrimaryKey(
                name: "pk_authors",
                table: "authors");

            migrationBuilder.DropPrimaryKey(
                name: "pk_author_roles",
                table: "author_roles");

            migrationBuilder.RenameTable(
                name: "roles",
                newName: "chat_roles");

            migrationBuilder.RenameTable(
                name: "authors",
                newName: "chat_authors");

            migrationBuilder.RenameTable(
                name: "author_roles",
                newName: "chat_author_roles");

            migrationBuilder.RenameIndex(
                name: "ix_roles_chat_id_name",
                table: "chat_roles",
                newName: "ix_chat_roles_chat_id_name");

            migrationBuilder.RenameIndex(
                name: "ix_roles_chat_id_local_id",
                table: "chat_roles",
                newName: "ix_chat_roles_chat_id_local_id");

            migrationBuilder.RenameIndex(
                name: "ix_authors_chat_id_user_id",
                table: "chat_authors",
                newName: "ix_chat_authors_chat_id_user_id");

            migrationBuilder.RenameIndex(
                name: "ix_authors_chat_id_local_id",
                table: "chat_authors",
                newName: "ix_chat_authors_chat_id_local_id");

            migrationBuilder.RenameIndex(
                name: "ix_author_roles_role_id_author_id",
                table: "chat_author_roles",
                newName: "ix_chat_author_roles_role_id_author_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_chat_roles",
                table: "chat_roles",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_chat_authors",
                table: "chat_authors",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_chat_author_roles",
                table: "chat_author_roles",
                columns: new[] { "author_id", "role_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_chat_author_roles_chat_authors_author_id",
                table: "chat_author_roles",
                column: "author_id",
                principalTable: "chat_authors",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
