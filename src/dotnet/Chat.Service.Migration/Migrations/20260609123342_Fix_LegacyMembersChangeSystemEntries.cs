using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class Fix_LegacyMembersChangeSystemEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A handful of system entries predate the current format and use an even
            // older "membersChange" shape that was never migrated to the LegacySystemEntry
            // schema. It binds neither MembersChanged nor NotifyMembers, so Option is null
            // and DbChatEntry.ToModel() throws "Unknown system entry option", which crashes
            // GetBatch in the content-indexing flows. Rebuild those rows as a valid
            // MembersChanged option, preserving authorId/hasLeft and filling the absent
            // authorName with "N/A".
            migrationBuilder.Sql(
                """
                UPDATE chat_entries
                SET content = jsonb_build_object(
                        'membersChanged', jsonb_build_object(
                            'authorId',   content::jsonb #>> '{membersChange,authorId}',
                            'authorName', 'N/A',
                            'hasLeft',    coalesce((content::jsonb #>> '{membersChange,hasLeft}')::boolean, false)
                        )
                    )::text
                WHERE is_system_entry
                  AND kind = 0
                  AND content LIKE '{"membersChange":%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // One-way data repair: reintroducing the typo would serve no purpose.
        }
    }
}
