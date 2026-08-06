using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Notifications.Migrations
{
    /// <inheritdoc />
    public partial class DropNotificationsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notifications");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    author_id = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    chat_entry_lid = table.Column<long>(type: "bigint", nullable: true),
                    chat_id = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    content = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    handled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    icon_url = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    similarity_key = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    title = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_id",
                table: "notifications",
                columns: new[] { "user_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_kind_similarity_key",
                table: "notifications",
                columns: new[] { "user_id", "kind", "similarity_key" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_version",
                table: "notifications",
                columns: new[] { "user_id", "version" });
        }
    }
}
