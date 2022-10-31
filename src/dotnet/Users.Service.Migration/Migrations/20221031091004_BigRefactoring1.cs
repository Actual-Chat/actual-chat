using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    public partial class BigRefactoring1 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_favorite",
                table: "user_contacts");

            migrationBuilder.DropColumn(
                name: "name",
                table: "user_contacts");

            migrationBuilder.DropColumn(
                name: "local_id",
                table: "user_avatars");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "user_avatars");

            migrationBuilder.DropColumn(
                name: "avatar_id",
                table: "accounts");

            migrationBuilder.AlterColumn<string>(
                name: "target_user_id",
                table: "user_contacts",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "principal_id",
                table: "user_avatars",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "principal_id",
                table: "user_avatars");

            migrationBuilder.AlterColumn<string>(
                name: "target_user_id",
                table: "user_contacts",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_favorite",
                table: "user_contacts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "name",
                table: "user_contacts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "local_id",
                table: "user_avatars",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "user_id",
                table: "user_avatars",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "avatar_id",
                table: "accounts",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
