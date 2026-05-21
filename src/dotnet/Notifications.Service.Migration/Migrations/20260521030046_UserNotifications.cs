using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Notifications.Migrations
{
    /// <inheritdoc />
    public partial class UserNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_notifications",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    data = table.Column<byte[]>(type: "bytea", nullable: false),
                    has_unsent_delta = table.Column<bool>(type: "boolean", nullable: false),
                    is_dormant = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_notifications", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_notifications");
        }
    }
}
