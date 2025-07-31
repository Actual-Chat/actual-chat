using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class MakeTranslationStreamIdNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_realtime",
                table: "translations");

            migrationBuilder.AlterColumn<string>(
                name: "stream_id",
                table: "translations",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "entry_id",
                table: "translations",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            // Update empty string values to null
            migrationBuilder.Sql("UPDATE translations SET stream_id = NULL WHERE stream_id = ''");
            migrationBuilder.Sql("UPDATE translations SET entry_id = NULL WHERE entry_id = ''");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Convert null values back to empty strings before making column non-nullable
            migrationBuilder.Sql("UPDATE translations SET stream_id = '' WHERE stream_id IS NULL");
            migrationBuilder.Sql("UPDATE translations SET entry_id = '' WHERE entry_id IS NULL");

            migrationBuilder.AlterColumn<string>(
                name: "stream_id",
                table: "translations",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "entry_id",
                table: "translations",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_realtime",
                table: "translations",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
