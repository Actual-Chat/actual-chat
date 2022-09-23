using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    public partial class Mentions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mentions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    author_id = table.Column<string>(type: "text", nullable: false),
                    chat_id = table.Column<string>(type: "text", nullable: false),
                    entry_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mentions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mentions_chat_id_author_id_entry_id",
                table: "mentions",
                columns: new[] { "chat_id", "author_id", "entry_id" });

            migrationBuilder.CreateIndex(
                name: "ix_mentions_chat_id_entry_id_author_id",
                table: "mentions",
                columns: new[] { "chat_id", "entry_id", "author_id" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mentions");
        }
    }
}
