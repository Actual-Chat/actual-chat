using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class chatroulette : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chat_roulettes",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    chat_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    profile_id1 = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    profile_id2 = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    user_id1 = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    user_id2 = table.Column<string>(type: "text", nullable: false, collation: "C")
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_roulettes");
        }
    }
}
