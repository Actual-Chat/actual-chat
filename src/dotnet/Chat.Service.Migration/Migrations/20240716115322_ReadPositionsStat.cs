using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class ReadPositionsStat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "read_positions_stat",
                columns: table => new
                {
                    chat_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    start_tracking_entry_lid = table.Column<long>(type: "bigint", nullable: false),
                    top1_entry_lid = table.Column<long>(type: "bigint", nullable: false),
                    top1_user_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    top2_entry_lid = table.Column<long>(type: "bigint", nullable: false),
                    top2_user_id = table.Column<string>(type: "text", nullable: false, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_read_positions_stat", x => x.chat_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "read_positions_stat");
        }
    }
}
