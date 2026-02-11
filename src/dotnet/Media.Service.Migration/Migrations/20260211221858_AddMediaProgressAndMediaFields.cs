using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Media.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaProgressAndMediaFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "user_id",
                table: "media",
                type: "text",
                nullable: false,
                defaultValue: "",
                collation: "C");

            migrationBuilder.AddColumn<long>(
                name: "version",
                table: "media",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "media_progresses",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    stage = table.Column<int>(type: "integer", nullable: false),
                    stage_progress = table.Column<double>(type: "double precision", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media_progresses", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "media_progresses");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "media");

            migrationBuilder.DropColumn(
                name: "version",
                table: "media");
        }
    }
}
