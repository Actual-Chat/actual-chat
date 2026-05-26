using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class Fix_ChatEntry_AudioId_Collation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // audio_id was added by Add_ChatEntry_AudioId, but ChatEntry_Refactoring's
            // designer snapshot lost the property due to historical drift, so EF tooling
            // wants to AddColumn it. The column actually exists on every DB — we only
            // need to bring its collation in line with the current model.
            migrationBuilder.AlterColumn<string>(
                name: "audio_id",
                table: "chat_entries",
                type: "text",
                nullable: true,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "audio_id",
                table: "chat_entries",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldCollation: "C");
        }
    }
}
