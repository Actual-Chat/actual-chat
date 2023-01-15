using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    public partial class RenameRecencyScope : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
update recent_entries
set scope = 'Chat'
where scope = 'ChatContact';
");

            migrationBuilder.Sql($@"
update recent_entries
set scope = 'Contact'
where scope = 'UserContact';
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
update recent_entries
set scope = 'ChatContact'
where scope = 'Chat';
");

            migrationBuilder.Sql($@"
update recent_entries
set scope = 'UserContact'
where scope = 'Contact';
");
        }
    }
}
