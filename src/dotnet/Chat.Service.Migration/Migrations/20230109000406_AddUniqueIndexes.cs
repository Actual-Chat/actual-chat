using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_roles_chat_id_local_id",
                table: "roles");

            migrationBuilder.DropIndex(
                name: "ix_chat_entries_chat_id_kind_local_id",
                table: "chat_entries");

            migrationBuilder.DropIndex(
                name: "ix_authors_chat_id_local_id",
                table: "authors");

            migrationBuilder.DropIndex(
                name: "ix_authors_chat_id_user_id",
                table: "authors");

            migrationBuilder.CreateIndex(
                name: "ix_roles_chat_id_local_id",
                table: "roles",
                columns: new[] { "chat_id", "local_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chat_entries_chat_id_kind_local_id",
                table: "chat_entries",
                columns: new[] { "chat_id", "kind", "local_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_authors_chat_id_local_id",
                table: "authors",
                columns: new[] { "chat_id", "local_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_authors_chat_id_user_id",
                table: "authors",
                columns: new[] { "chat_id", "user_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_roles_chat_id_local_id",
                table: "roles");

            migrationBuilder.DropIndex(
                name: "ix_chat_entries_chat_id_kind_local_id",
                table: "chat_entries");

            migrationBuilder.DropIndex(
                name: "ix_authors_chat_id_local_id",
                table: "authors");

            migrationBuilder.DropIndex(
                name: "ix_authors_chat_id_user_id",
                table: "authors");

            migrationBuilder.CreateIndex(
                name: "ix_roles_chat_id_local_id",
                table: "roles",
                columns: new[] { "chat_id", "local_id" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_entries_chat_id_kind_local_id",
                table: "chat_entries",
                columns: new[] { "chat_id", "kind", "local_id" });

            migrationBuilder.CreateIndex(
                name: "ix_authors_chat_id_local_id",
                table: "authors",
                columns: new[] { "chat_id", "local_id" });

            migrationBuilder.CreateIndex(
                name: "ix_authors_chat_id_user_id",
                table: "authors",
                columns: new[] { "chat_id", "user_id" });
        }
    }
}
