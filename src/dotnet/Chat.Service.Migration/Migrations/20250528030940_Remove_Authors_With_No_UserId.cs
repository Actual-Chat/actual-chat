using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class Remove_Authors_With_No_UserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("delete from authors where coalesce(user_id, '') = ''");

            migrationBuilder.AlterColumn<string>(
                name: "user_id",
                table: "authors",
                type: "text",
                nullable: false,
                defaultValue: "",
                collation: "C",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldCollation: "C");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "user_id",
                table: "authors",
                type: "text",
                nullable: true,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "text",
                oldCollation: "C");
        }
    }
}
