using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations;

/// <inheritdoc />
public partial class _20261008013151_Add_ChatCleanup : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<TimeSpan>(
            name: "retention_period",
            table: "chats",
            type: "interval",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "is_removed_and_purged",
            table: "chat_entries",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTime>(
            name: "removed_at",
            table: "chat_entries",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_chat_entries_chat_id_kind_is_removed_removed_at",
            table: "chat_entries",
            columns: new[] { "chat_id", "kind", "is_removed", "removed_at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_chat_entries_chat_id_kind_is_removed_removed_at",
            table: "chat_entries");

        migrationBuilder.DropColumn(
            name: "retention_period",
            table: "chats");

        migrationBuilder.DropColumn(
            name: "is_removed_and_purged",
            table: "chat_entries");

        migrationBuilder.DropColumn(
            name: "removed_at",
            table: "chat_entries");
    }
}
