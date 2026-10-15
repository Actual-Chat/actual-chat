using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations;

/// <inheritdoc />
public partial class _20261014091148_RemoveUnusedChatEntryColumns : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Legacy audio entries (kind != 0) share local_id values with text entries, so they must go
        // before the unique (chat_id, local_id) index is created and before the kind column is dropped.
        migrationBuilder.Sql("DELETE FROM chat_entries WHERE kind <> 0;");

        migrationBuilder.DropIndex(
            name: "IX_chat_entries_audio_id",
            table: "chat_entries");

        migrationBuilder.DropIndex(
            name: "IX_chat_entries_content_stream_id",
            table: "chat_entries");

        migrationBuilder.DropIndex(
            name: "ix_chat_entries_chat_id_kind_begins_at_ends_at",
            table: "chat_entries");

        migrationBuilder.DropIndex(
            name: "ix_chat_entries_chat_id_kind_ends_at_begins_at",
            table: "chat_entries");

        migrationBuilder.DropIndex(
            name: "ix_chat_entries_chat_id_kind_is_removed_local_id",
            table: "chat_entries");

        migrationBuilder.DropIndex(
            name: "ix_chat_entries_chat_id_kind_is_removed_removed_at",
            table: "chat_entries");

        migrationBuilder.DropIndex(
            name: "ix_chat_entries_chat_id_kind_local_id",
            table: "chat_entries");

        migrationBuilder.DropIndex(
            name: "ix_chat_entries_chat_id_kind_version",
            table: "chat_entries");

        migrationBuilder.DropIndex(
            name: "ix_chat_entries_chat_id_version_local_id",
            table: "chat_entries");

        migrationBuilder.DropColumn(
            name: "audio_entry_id",
            table: "chat_entries");

        migrationBuilder.DropColumn(
            name: "client_side_begins_at",
            table: "chat_entries");

        migrationBuilder.DropColumn(
            name: "content_ends_at",
            table: "chat_entries");

        migrationBuilder.DropColumn(
            name: "duration",
            table: "chat_entries");

        migrationBuilder.DropColumn(
            name: "kind",
            table: "chat_entries");

        migrationBuilder.CreateIndex(
            name: "IX_chat_entries_audio_id",
            table: "chat_entries",
            column: "audio_id",
            filter: "\"audio_id\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_chat_entries_content_stream_id",
            table: "chat_entries",
            column: "content_stream_id",
            filter: "\"content_stream_id\" <> ''");

        migrationBuilder.CreateIndex(
            name: "ix_chat_entries_chat_id_begins_at_ends_at",
            table: "chat_entries",
            columns: new[] { "chat_id", "begins_at", "ends_at" });

        migrationBuilder.CreateIndex(
            name: "ix_chat_entries_chat_id_ends_at_begins_at",
            table: "chat_entries",
            columns: new[] { "chat_id", "ends_at", "begins_at" });

        migrationBuilder.CreateIndex(
            name: "ix_chat_entries_chat_id_is_removed_local_id",
            table: "chat_entries",
            columns: new[] { "chat_id", "is_removed", "local_id" });

        migrationBuilder.CreateIndex(
            name: "ix_chat_entries_chat_id_is_removed_removed_at",
            table: "chat_entries",
            columns: new[] { "chat_id", "is_removed", "removed_at" });

        migrationBuilder.CreateIndex(
            name: "ix_chat_entries_chat_id_local_id",
            table: "chat_entries",
            columns: new[] { "chat_id", "local_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_chat_entries_chat_id_version_local_id",
            table: "chat_entries",
            columns: new[] { "chat_id", "version", "local_id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_chat_entries_audio_id",
            table: "chat_entries");

        migrationBuilder.DropIndex(
            name: "IX_chat_entries_content_stream_id",
            table: "chat_entries");

        migrationBuilder.DropIndex(
            name: "ix_chat_entries_chat_id_begins_at_ends_at",
            table: "chat_entries");

        migrationBuilder.DropIndex(
            name: "ix_chat_entries_chat_id_ends_at_begins_at",
            table: "chat_entries");

        migrationBuilder.DropIndex(
            name: "ix_chat_entries_chat_id_is_removed_local_id",
            table: "chat_entries");

        migrationBuilder.DropIndex(
            name: "ix_chat_entries_chat_id_is_removed_removed_at",
            table: "chat_entries");

        migrationBuilder.DropIndex(
            name: "ix_chat_entries_chat_id_local_id",
            table: "chat_entries");

        migrationBuilder.DropIndex(
            name: "ix_chat_entries_chat_id_version_local_id",
            table: "chat_entries");

        migrationBuilder.AddColumn<long>(
            name: "audio_entry_id",
            table: "chat_entries",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "client_side_begins_at",
            table: "chat_entries",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "content_ends_at",
            table: "chat_entries",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "duration",
            table: "chat_entries",
            type: "double precision",
            nullable: false,
            defaultValue: 0.0);

        migrationBuilder.AddColumn<int>(
            name: "kind",
            table: "chat_entries",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.CreateIndex(
            name: "IX_chat_entries_audio_id",
            table: "chat_entries",
            column: "audio_id",
            filter: "\"kind\" = 0 AND \"audio_id\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_chat_entries_content_stream_id",
            table: "chat_entries",
            column: "content_stream_id",
            filter: "\"kind\" = 0 AND \"content_stream_id\" <> ''");

        migrationBuilder.CreateIndex(
            name: "ix_chat_entries_chat_id_kind_begins_at_ends_at",
            table: "chat_entries",
            columns: new[] { "chat_id", "kind", "begins_at", "ends_at" });

        migrationBuilder.CreateIndex(
            name: "ix_chat_entries_chat_id_kind_ends_at_begins_at",
            table: "chat_entries",
            columns: new[] { "chat_id", "kind", "ends_at", "begins_at" });

        migrationBuilder.CreateIndex(
            name: "ix_chat_entries_chat_id_kind_is_removed_local_id",
            table: "chat_entries",
            columns: new[] { "chat_id", "kind", "is_removed", "local_id" });

        migrationBuilder.CreateIndex(
            name: "ix_chat_entries_chat_id_kind_is_removed_removed_at",
            table: "chat_entries",
            columns: new[] { "chat_id", "kind", "is_removed", "removed_at" });

        migrationBuilder.CreateIndex(
            name: "ix_chat_entries_chat_id_kind_local_id",
            table: "chat_entries",
            columns: new[] { "chat_id", "kind", "local_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_chat_entries_chat_id_kind_version",
            table: "chat_entries",
            columns: new[] { "chat_id", "kind", "version" });

        migrationBuilder.CreateIndex(
            name: "ix_chat_entries_chat_id_version_local_id",
            table: "chat_entries",
            columns: new[] { "chat_id", "version", "local_id" },
            filter: "kind = 0");
    }
}
