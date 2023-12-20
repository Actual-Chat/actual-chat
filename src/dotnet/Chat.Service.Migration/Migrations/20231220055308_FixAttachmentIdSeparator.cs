using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class FixAttachmentIdSeparator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                                 update
                                 text_entry_attachments
                                 set id = substring(id, 1, length(id) - 3) || ':' || substring(id, length(id), 1)
                                 where id like '%58_';
                                 """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // no need to revert back to incorrect format
        }
    }
}
