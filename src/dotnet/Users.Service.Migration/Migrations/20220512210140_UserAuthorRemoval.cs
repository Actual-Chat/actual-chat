using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    public partial class UserAuthorRemoval : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserAuthors");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "UserProfiles",
                newName: "UserId");

            migrationBuilder.AddColumn<string>(
                name: "AvatarId",
                table: "UserProfiles",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvatarId",
                table: "UserProfiles");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "UserProfiles",
                newName: "Id");

            migrationBuilder.CreateTable(
                name: "UserAuthors",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AvatarId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsAnonymous = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAuthors", x => x.UserId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }
    }
}
