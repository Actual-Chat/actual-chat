using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Notification.Migrations.Migrations
{
    public partial class AddMessagesIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Messages_DeviceId",
                table: "Messages",
                column: "DeviceId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Messages_DeviceId",
                table: "Messages");
        }
    }
}
