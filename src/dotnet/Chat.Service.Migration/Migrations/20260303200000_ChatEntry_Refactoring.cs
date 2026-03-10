using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class ChatEntry_Refactoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "has_attachment_uploads",
                table: "chat_entries");

            migrationBuilder.DropColumn(
                name: "video_entry_id",
                table: "chat_entries");

            migrationBuilder.RenameColumn(
                name: "entry_local_id",
                table: "mentions",
                newName: "entry_lid");

            migrationBuilder.RenameIndex(
                name: "ix_mentions_chat_id_mention_id_entry_local_id",
                table: "mentions",
                newName: "ix_mentions_chat_id_mention_id_entry_lid");

            migrationBuilder.RenameIndex(
                name: "ix_mentions_chat_id_entry_local_id_mention_id",
                table: "mentions",
                newName: "ix_mentions_chat_id_entry_lid_mention_id");

            migrationBuilder.RenameColumn(
                name: "stream_id",
                table: "chat_entries",
                newName: "content_stream_id");

            migrationBuilder.RenameIndex(
                name: "IX_chat_entries_stream_id",
                table: "chat_entries",
                newName: "IX_chat_entries_content_stream_id");

            migrationBuilder.DropPrimaryKey(
                name: "pk_text_entry_attachments",
                table: "text_entry_attachments");

            migrationBuilder.RenameTable(
                name: "text_entry_attachments",
                newName: "chat_entry_attachments");

            migrationBuilder.AddPrimaryKey(
                name: "pk_chat_entry_attachments",
                table: "chat_entry_attachments",
                column: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_chat_entry_attachments",
                table: "chat_entry_attachments");

            migrationBuilder.RenameTable(
                name: "chat_entry_attachments",
                newName: "text_entry_attachments");

            migrationBuilder.AddPrimaryKey(
                name: "pk_text_entry_attachments",
                table: "text_entry_attachments",
                column: "id");

            migrationBuilder.RenameColumn(
                name: "entry_lid",
                table: "mentions",
                newName: "entry_local_id");

            migrationBuilder.RenameIndex(
                name: "ix_mentions_chat_id_mention_id_entry_lid",
                table: "mentions",
                newName: "ix_mentions_chat_id_mention_id_entry_local_id");

            migrationBuilder.RenameIndex(
                name: "ix_mentions_chat_id_entry_lid_mention_id",
                table: "mentions",
                newName: "ix_mentions_chat_id_entry_local_id_mention_id");

            migrationBuilder.RenameColumn(
                name: "content_stream_id",
                table: "chat_entries",
                newName: "stream_id");

            migrationBuilder.RenameIndex(
                name: "IX_chat_entries_content_stream_id",
                table: "chat_entries",
                newName: "IX_chat_entries_stream_id");

            migrationBuilder.AddColumn<bool>(
                name: "has_attachment_uploads",
                table: "chat_entries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "video_entry_id",
                table: "chat_entries",
                type: "bigint",
                nullable: true);
        }
    }
}
