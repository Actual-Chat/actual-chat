using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class MultipleLinkPreviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "link_preview_id",
                table: "chat_entries",
                newName: "link_preview_ids");

            migrationBuilder.Sql("""
                                 update chat_entries
                                 set link_preview_ids = '["' || chat_entries.link_preview_ids || '"]'
                                 where link_preview_ids != '' and chat_entries.link_preview_ids not like '[%';
                                 """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                                 update chat_entries
                                 set link_preview_ids = left(right(link_preview_ids, -2), -2)
                                 where link_preview_ids != '' and link_preview_ids like '["%'
                                 """);
            migrationBuilder.RenameColumn(
                name: "link_preview_ids",
                table: "chat_entries",
                newName: "link_preview_id");
        }
    }
}
