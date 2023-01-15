using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    public partial class ChatEntryId : Migration
    {
               protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "chat_entry_id",
                table: "text_entry_attachments",
                type: "text",
                nullable: false,
                defaultValue: "");
            migrationBuilder.Sql(@"
update text_entry_attachments
set chat_entry_id =
        (select chat_entries.composite_id
         from chat_entries
         where chat_entries.chat_id = text_entry_attachments.chat_id
           and chat_entries.id = text_entry_attachments.entry_id
         limit 1)
");
            migrationBuilder.DropColumn(
                name: "entry_id",
                table: "text_entry_attachments");
            migrationBuilder.DropColumn(
                name: "chat_id",
                table: "text_entry_attachments");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "entry_id",
                table: "text_entry_attachments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
            migrationBuilder.AddColumn<string>(
                name: "chat_id",
                table: "text_entry_attachments",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(@"
update text_entry_attachments
set chat_id =
        (select chat_entries.chat_id
        from chat_entries
        where chat_entries.composite_id = text_entry_attachments.chat_entry_id
        limit 1),
    entry_id =
        (select chat_entries.id
        from chat_entries
        where chat_entries.composite_id = text_entry_attachments.chat_entry_id
        limit 1)
");

            migrationBuilder.DropColumn(
                name: "chat_entry_id",
                table: "text_entry_attachments");
        }
    }
}
