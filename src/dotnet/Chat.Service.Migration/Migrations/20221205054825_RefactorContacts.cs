using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class RefactorContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_reaction_summaries_chat_entry_id",
                table: "reaction_summaries");

            migrationBuilder.DropPrimaryKey(
                name: "pk_chat_entries",
                table: "chat_entries");

            migrationBuilder.DropIndex(
                name: "ix_chat_entries_chat_id_type_id",
                table: "chat_entries");

            migrationBuilder.DropIndex(
                name: "ix_chat_entries_chat_id_type_is_removed_id",
                table: "chat_entries");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "chat_entries",
                newName: "local_id");

            migrationBuilder.RenameColumn(
                name: "composite_id",
                table: "chat_entries",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "chat_entry_id",
                table: "text_entry_attachments",
                newName: "entry_id");

            migrationBuilder.RenameColumn(
                name: "composite_id",
                table: "text_entry_attachments",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "emoji",
                table: "reactions",
                newName: "emoji_id");

            migrationBuilder.RenameColumn(
                name: "chat_entry_id",
                table: "reactions",
                newName: "entry_id");

            migrationBuilder.RenameColumn(
                name: "emoji",
                table: "reaction_summaries",
                newName: "emoji_id");

            migrationBuilder.RenameColumn(
                name: "chat_entry_id",
                table: "reaction_summaries",
                newName: "entry_id");

            migrationBuilder.RenameColumn(
                name: "author_id",
                table: "mentions",
                newName: "mention_id");

            migrationBuilder.RenameIndex(
                name: "ix_mentions_chat_id_entry_id_author_id",
                table: "mentions",
                newName: "ix_mentions_chat_id_entry_id_mention_id");

            migrationBuilder.RenameIndex(
                name: "ix_mentions_chat_id_author_id_entry_id",
                table: "mentions",
                newName: "ix_mentions_chat_id_mention_id_entry_id");

            migrationBuilder.RenameColumn(
                name: "chat_type",
                table: "chats",
                newName: "kind");

            migrationBuilder.RenameColumn(
                name: "type",
                table: "chat_entries",
                newName: "kind");

            migrationBuilder.RenameIndex(
                name: "ix_chat_entries_chat_id_type_version",
                table: "chat_entries",
                newName: "ix_chat_entries_chat_id_kind_version");

            migrationBuilder.RenameIndex(
                name: "ix_chat_entries_chat_id_type_ends_at_begins_at",
                table: "chat_entries",
                newName: "ix_chat_entries_chat_id_kind_ends_at_begins_at");

            migrationBuilder.RenameIndex(
                name: "ix_chat_entries_chat_id_type_begins_at_ends_at",
                table: "chat_entries",
                newName: "ix_chat_entries_chat_id_kind_begins_at_ends_at");

            migrationBuilder.AlterColumn<DateTime>(
                name: "modified_at",
                table: "reactions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "pk_chat_entries",
                table: "chat_entries",
                column: "id");

            migrationBuilder.CreateIndex(
                name: "ix_reaction_summaries_entry_id",
                table: "reaction_summaries",
                column: "entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_entries_chat_id_kind_is_removed_local_id",
                table: "chat_entries",
                columns: new[] { "chat_id", "kind", "is_removed", "local_id" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_entries_chat_id_kind_local_id",
                table: "chat_entries",
                columns: new[] { "chat_id", "kind", "local_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_reaction_summaries_entry_id",
                table: "reaction_summaries");

            migrationBuilder.DropPrimaryKey(
                name: "pk_chat_entries",
                table: "chat_entries");

            migrationBuilder.DropIndex(
                name: "ix_chat_entries_chat_id_kind_is_removed_local_id",
                table: "chat_entries");

            migrationBuilder.DropIndex(
                name: "ix_chat_entries_chat_id_kind_local_id",
                table: "chat_entries");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "chat_entries",
                newName: "composite_id");

            migrationBuilder.RenameColumn(
                name: "local_id",
                table: "chat_entries",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "entry_id",
                table: "text_entry_attachments",
                newName: "chat_entry_id");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "text_entry_attachments",
                newName: "composite_id");

            migrationBuilder.RenameColumn(
                name: "emoji_id",
                table: "reactions",
                newName: "emoji");

            migrationBuilder.RenameColumn(
                name: "entry_id",
                table: "reactions",
                newName: "chat_entry_id");

            migrationBuilder.RenameColumn(
                name: "emoji_id",
                table: "reaction_summaries",
                newName: "emoji");

            migrationBuilder.RenameColumn(
                name: "entry_id",
                table: "reaction_summaries",
                newName: "chat_entry_id");

            migrationBuilder.RenameColumn(
                name: "mention_id",
                table: "mentions",
                newName: "author_id");

            migrationBuilder.RenameIndex(
                name: "ix_mentions_chat_id_mention_id_entry_id",
                table: "mentions",
                newName: "ix_mentions_chat_id_author_id_entry_id");

            migrationBuilder.RenameIndex(
                name: "ix_mentions_chat_id_entry_id_mention_id",
                table: "mentions",
                newName: "ix_mentions_chat_id_entry_id_author_id");

            migrationBuilder.RenameColumn(
                name: "kind",
                table: "chats",
                newName: "chat_type");

            migrationBuilder.RenameColumn(
                name: "kind",
                table: "chat_entries",
                newName: "type");

            migrationBuilder.RenameIndex(
                name: "ix_chat_entries_chat_id_kind_version",
                table: "chat_entries",
                newName: "ix_chat_entries_chat_id_type_version");

            migrationBuilder.RenameIndex(
                name: "ix_chat_entries_chat_id_kind_ends_at_begins_at",
                table: "chat_entries",
                newName: "ix_chat_entries_chat_id_type_ends_at_begins_at");

            migrationBuilder.RenameIndex(
                name: "ix_chat_entries_chat_id_kind_begins_at_ends_at",
                table: "chat_entries",
                newName: "ix_chat_entries_chat_id_type_begins_at_ends_at");

            migrationBuilder.AlterColumn<DateTime>(
                name: "modified_at",
                table: "reactions",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddPrimaryKey(
                name: "pk_chat_entries",
                table: "chat_entries",
                column: "composite_id");

            migrationBuilder.CreateIndex(
                name: "ix_reaction_summaries_chat_entry_id",
                table: "reaction_summaries",
                column: "chat_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_entries_chat_id_type_id",
                table: "chat_entries",
                columns: new[] { "chat_id", "type", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_entries_chat_id_type_is_removed_id",
                table: "chat_entries",
                columns: new[] { "chat_id", "type", "is_removed", "id" });
        }
    }
}
