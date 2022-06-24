using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Notification.Migrations
{
    public partial class MutedChatSubscriptions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "disabled_chat_subscriptions");

            migrationBuilder.CreateTable(
                name: "muted_chat_subscriptions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    chat_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_muted_chat_subscriptions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_muted_chat_subscriptions_user_id_chat_id",
                table: "muted_chat_subscriptions",
                columns: new[] { "user_id", "chat_id" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "muted_chat_subscriptions");

            migrationBuilder.CreateTable(
                name: "disabled_chat_subscriptions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    chat_id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_disabled_chat_subscriptions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_disabled_chat_subscriptions_user_id_chat_id",
                table: "disabled_chat_subscriptions",
                columns: new[] { "user_id", "chat_id" });
        }
    }
}
