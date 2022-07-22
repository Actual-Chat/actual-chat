using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    public partial class UpdateChatRoles2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "author_ids",
                table: "chat_roles");

            migrationBuilder.AddColumn<bool>(
                name: "can_edit_properties",
                table: "chat_roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "can_invite",
                table: "chat_roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "can_read",
                table: "chat_roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "can_write",
                table: "chat_roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<short>(
                name: "system_role",
                table: "chat_roles",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.CreateTable(
                name: "chat_author_roles",
                columns: table => new
                {
                    chat_author_id = table.Column<string>(type: "text", nullable: false),
                    chat_role_id = table.Column<string>(type: "text", nullable: false),
                    db_chat_author_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_author_roles", x => new { x.chat_author_id, x.chat_role_id });
                    table.ForeignKey(
                        name: "fk_chat_author_roles_chat_authors_db_chat_author_id",
                        column: x => x.db_chat_author_id,
                        principalTable: "chat_authors",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_chat_author_roles_chat_role_id_chat_author_id",
                table: "chat_author_roles",
                columns: new[] { "chat_role_id", "chat_author_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chat_author_roles_db_chat_author_id",
                table: "chat_author_roles",
                column: "db_chat_author_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_author_roles");

            migrationBuilder.DropColumn(
                name: "can_edit_properties",
                table: "chat_roles");

            migrationBuilder.DropColumn(
                name: "can_invite",
                table: "chat_roles");

            migrationBuilder.DropColumn(
                name: "can_read",
                table: "chat_roles");

            migrationBuilder.DropColumn(
                name: "can_write",
                table: "chat_roles");

            migrationBuilder.DropColumn(
                name: "system_role",
                table: "chat_roles");

            migrationBuilder.AddColumn<string>(
                name: "author_ids",
                table: "chat_roles",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
