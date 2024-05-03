using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Notification.Migrations
{
    /// <inheritdoc />
    public partial class AddCollationToIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "user_id",
                table: "notifications",
                type: "text",
                nullable: false,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "similarity_key",
                table: "notifications",
                type: "text",
                nullable: false,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "chat_id",
                table: "notifications",
                type: "text",
                nullable: true,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "author_id",
                table: "notifications",
                type: "text",
                nullable: true,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "id",
                table: "notifications",
                type: "text",
                nullable: false,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "user_id",
                table: "devices",
                type: "text",
                nullable: false,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "id",
                table: "devices",
                type: "text",
                nullable: false,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "uuid",
                table: "_operations",
                type: "text",
                nullable: false,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "host_id",
                table: "_operations",
                type: "text",
                nullable: false,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "uuid",
                table: "_events",
                type: "text",
                nullable: false,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "text");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "user_id",
                table: "notifications",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldCollation: "C");

            migrationBuilder.AlterColumn<string>(
                name: "similarity_key",
                table: "notifications",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldCollation: "C");

            migrationBuilder.AlterColumn<string>(
                name: "chat_id",
                table: "notifications",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldCollation: "C");

            migrationBuilder.AlterColumn<string>(
                name: "author_id",
                table: "notifications",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldCollation: "C");

            migrationBuilder.AlterColumn<string>(
                name: "id",
                table: "notifications",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldCollation: "C");

            migrationBuilder.AlterColumn<string>(
                name: "user_id",
                table: "devices",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldCollation: "C");

            migrationBuilder.AlterColumn<string>(
                name: "id",
                table: "devices",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldCollation: "C");

            migrationBuilder.AlterColumn<string>(
                name: "uuid",
                table: "_operations",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldCollation: "C");

            migrationBuilder.AlterColumn<string>(
                name: "host_id",
                table: "_operations",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldCollation: "C");

            migrationBuilder.AlterColumn<string>(
                name: "uuid",
                table: "_events",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldCollation: "C");
        }
    }
}
