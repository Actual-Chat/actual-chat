using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    public partial class MessageReply : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "replied_chat_entry_id",
                table: "chat_entries",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "chat_roles",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    chat_id = table.Column<string>(type: "text", nullable: false),
                    local_id = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    picture = table.Column<string>(type: "text", nullable: false),
                    principal_ids = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_roles", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chat_roles_chat_id_local_id",
                table: "chat_roles",
                columns: new[] { "chat_id", "local_id" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_roles_chat_id_name",
                table: "chat_roles",
                columns: new[] { "chat_id", "name" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_roles");

            migrationBuilder.DropColumn(
                name: "replied_chat_entry_id",
                table: "chat_entries");
        }
    }
}
