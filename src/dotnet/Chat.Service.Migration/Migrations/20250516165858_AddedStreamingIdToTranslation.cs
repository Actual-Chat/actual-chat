using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class AddedStreamingIdToTranslation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_streaming",
                table: "translations");

            migrationBuilder.AddColumn<string>(
                name: "stream_id",
                table: "translations",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "stream_id",
                table: "translations");

            migrationBuilder.AddColumn<bool>(
                name: "is_streaming",
                table: "translations",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
