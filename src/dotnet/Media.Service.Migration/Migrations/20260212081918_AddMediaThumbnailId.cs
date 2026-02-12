using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Media.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaThumbnailId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "thumbnail_id",
                table: "media",
                type: "text",
                nullable: false,
                defaultValue: "",
                collation: "C");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "thumbnail_id",
                table: "media");
        }
    }
}
