using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Invite.Migrations
{
    public partial class AddSearchKey : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Code",
                table: "Invites");

            migrationBuilder.AddColumn<string>(
                name: "SearchKey",
                table: "Invites",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Invites_SearchKey_Remaining",
                table: "Invites",
                columns: new[] { "SearchKey", "Remaining" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invites_SearchKey_Remaining",
                table: "Invites");

            migrationBuilder.DropColumn(
                name: "SearchKey",
                table: "Invites");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Invites",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
