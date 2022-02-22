using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations.Migrations
{
    public partial class UserAvatars : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Picture",
                table: "UserAuthors");

            migrationBuilder.AddColumn<string>(
                 name: "AvatarId",
                 table: "UserAuthors",
                 type: "text",
                 nullable: false,
                 defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AvatarId",
                table: "ChatUserSettings",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "UserAvatars",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    LocalId = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Picture = table.Column<string>(type: "text", nullable: false),
                    Bio = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAvatars", x => x.Id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserAvatars");

            migrationBuilder.DropColumn(
                name: "AvatarId",
                table: "ChatUserSettings");

            migrationBuilder.DropColumn(
                name: "AvatarId",
                table: "UserAuthors");

            migrationBuilder.AddColumn<string>(
                name: "Picture",
                table: "UserAuthors",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
