using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations;

/// <inheritdoc />
public partial class _20260819081048_Tighten_ChatEntry_ContentStreamId_Index : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_chat_entries_content_stream_id",
            table: "chat_entries");

        migrationBuilder.CreateIndex(
            name: "IX_chat_entries_content_stream_id",
            table: "chat_entries",
            column: "content_stream_id",
            filter: "\"kind\" = 0 AND \"content_stream_id\" <> ''");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_chat_entries_content_stream_id",
            table: "chat_entries");

        migrationBuilder.CreateIndex(
            name: "IX_chat_entries_content_stream_id",
            table: "chat_entries",
            column: "content_stream_id",
            filter: "\"kind\" = 0 AND \"content_stream_id\" IS NOT NULL");
    }
}
