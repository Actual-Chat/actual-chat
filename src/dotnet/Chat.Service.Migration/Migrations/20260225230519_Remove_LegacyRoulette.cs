using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class Remove_LegacyRoulette : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_roulettes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chat_roulettes",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    chat_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    complete_reason = table.Column<int>(type: "integer", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_by_user_id = table.Column<string>(type: "text", nullable: false),
                    initiated_by_user_id = table.Column<string>(type: "text", nullable: false),
                    profile_id1 = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    profile_id2 = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    user_id1 = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    user_id2 = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_roulettes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chat_roulettes_chat_id",
                table: "chat_roulettes",
                column: "chat_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chat_roulettes_profile_id1_profile_id2",
                table: "chat_roulettes",
                columns: new[] { "profile_id1", "profile_id2" },
                unique: true);
        }
    }
}
