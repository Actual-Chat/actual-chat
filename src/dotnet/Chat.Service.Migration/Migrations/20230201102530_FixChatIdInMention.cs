using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class FixChatIdInMention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
update mentions
set chat_id = split_part(id, ':', 1)
where (chat_id is null OR chat_id = '')
  and exists (select 1 from chats where id = split_part(id, ':', 1));
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        { }
    }
}
