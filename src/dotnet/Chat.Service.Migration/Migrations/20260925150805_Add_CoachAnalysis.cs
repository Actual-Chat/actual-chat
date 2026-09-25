using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations;

/// <inheritdoc />
public partial class _20260925150805_Add_CoachAnalysis : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "coach_conversations",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                version = table.Column<long>(type: "bigint", nullable: false),
                chat_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                start_entry_lid = table.Column<long>(type: "bigint", nullable: false),
                author_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                user_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                conversation_version = table.Column<long>(type: "bigint", nullable: false),
                ends_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                own_speech_seconds = table.Column<double>(type: "double precision", nullable: false),
                total_speech_seconds = table.Column<double>(type: "double precision", nullable: false),
                own_turns = table.Column<int>(type: "integer", nullable: false),
                total_turns = table.Column<int>(type: "integer", nullable: false),
                participants = table.Column<int>(type: "integer", nullable: false),
                longest_monologue_seconds = table.Column<double>(type: "double precision", nullable: false),
                responses = table.Column<int>(type: "integer", nullable: false),
                response_gap_seconds = table.Column<double>(type: "double precision", nullable: false),
                interruptions = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_coach_conversations", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "coach_entries",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                version = table.Column<long>(type: "bigint", nullable: false),
                chat_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                local_id = table.Column<long>(type: "bigint", nullable: false),
                author_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                user_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                begins_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                language = table.Column<string>(type: "text", nullable: true),
                duration_seconds = table.Column<double>(type: "double precision", nullable: false),
                speech_seconds = table.Column<double>(type: "double precision", nullable: true),
                words = table.Column<int>(type: "integer", nullable: true),
                sentences = table.Column<int>(type: "integer", nullable: true),
                questions = table.Column<int>(type: "integer", nullable: true),
                repetitions = table.Column<int>(type: "integer", nullable: true),
                distinct_words = table.Column<int>(type: "integer", nullable: true),
                pauses = table.Column<int>(type: "integer", nullable: true),
                pause_seconds = table.Column<double>(type: "double precision", nullable: true),
                spans = table.Column<string>(type: "text", nullable: false),
                filled_pauses = table.Column<int>(type: "integer", nullable: false),
                fillers = table.Column<int>(type: "integer", nullable: false),
                weak_words = table.Column<int>(type: "integer", nullable: false),
                profanities = table.Column<int>(type: "integer", nullable: false),
                tag_state = table.Column<int>(type: "integer", nullable: false),
                prompt_version = table.Column<int>(type: "integer", nullable: false),
                tagged_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                content_hash = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_coach_entries", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_coach_conversations_chat_id_author_id",
            table: "coach_conversations",
            columns: new[] { "chat_id", "author_id" });

        migrationBuilder.CreateIndex(
            name: "ix_coach_entries_chat_id_author_id_local_id",
            table: "coach_entries",
            columns: new[] { "chat_id", "author_id", "local_id" });

        migrationBuilder.CreateIndex(
            name: "ix_coach_entries_chat_id_tag_state",
            table: "coach_entries",
            columns: new[] { "chat_id", "tag_state" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "coach_conversations");

        migrationBuilder.DropTable(
            name: "coach_entries");
    }
}
