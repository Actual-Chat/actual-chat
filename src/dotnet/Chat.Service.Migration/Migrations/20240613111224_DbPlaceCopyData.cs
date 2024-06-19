using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class DbPlaceCopyData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var sql = """
                       INSERT into places (id, version, title, is_public, media_id, background_media_id, created_at)
                       SELECT SPLIT_PART(id, '-', 2) as id, version, title, is_public, media_id, '' as background_media_id, created_at
                       FROM chats
                       WHERE id like 's-%' and SPLIT_PART(id, '-', 2) = SPLIT_PART(id, '-', 3);
                      """;
            migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Do nothing
        }
    }
}
