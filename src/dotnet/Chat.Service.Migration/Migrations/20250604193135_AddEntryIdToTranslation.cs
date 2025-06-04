using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class AddEntryIdToTranslation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "entry_id",
                table: "translations",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "entry_id",
                table: "translations");
        }
    }
}
