using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Contacts.Migrations
{
    /// <inheritdoc />
    public partial class AddContactState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "state",
                table: "contacts",
                type: "integer",
                nullable: false,
                defaultValue: 0);
            migrationBuilder.Sql("UPDATE contacts SET state = 16;"); // 0x10 = ContactState.Regular
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "state",
                table: "contacts");
        }
    }
}
