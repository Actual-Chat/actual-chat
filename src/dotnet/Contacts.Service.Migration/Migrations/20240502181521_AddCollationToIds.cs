using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Contacts.Migrations
{
    /// <inheritdoc />
    public partial class AddCollationToIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "place_id",
                table: "place_contacts",
                type: "text",
                nullable: false,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "owner_id",
                table: "place_contacts",
                type: "text",
                nullable: false,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "id",
                table: "place_contacts",
                type: "text",
                nullable: false,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "id",
                table: "external_contacts_hashes",
                type: "text",
                nullable: false,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "id",
                table: "external_contacts",
                type: "text",
                nullable: false,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "external_contact_id",
                table: "external_contact_links",
                type: "text",
                nullable: false,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "user_id",
                table: "contacts",
                type: "text",
                nullable: true,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "place_id",
                table: "contacts",
                type: "text",
                nullable: true,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "owner_id",
                table: "contacts",
                type: "text",
                nullable: false,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "chat_id",
                table: "contacts",
                type: "text",
                nullable: true,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "id",
                table: "contacts",
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
                name: "place_id",
                table: "place_contacts",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldCollation: "C");

            migrationBuilder.AlterColumn<string>(
                name: "owner_id",
                table: "place_contacts",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldCollation: "C");

            migrationBuilder.AlterColumn<string>(
                name: "id",
                table: "place_contacts",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldCollation: "C");

            migrationBuilder.AlterColumn<string>(
                name: "id",
                table: "external_contacts_hashes",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldCollation: "C");

            migrationBuilder.AlterColumn<string>(
                name: "id",
                table: "external_contacts",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldCollation: "C");

            migrationBuilder.AlterColumn<string>(
                name: "external_contact_id",
                table: "external_contact_links",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldCollation: "C");

            migrationBuilder.AlterColumn<string>(
                name: "user_id",
                table: "contacts",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldCollation: "C");

            migrationBuilder.AlterColumn<string>(
                name: "place_id",
                table: "contacts",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldCollation: "C");

            migrationBuilder.AlterColumn<string>(
                name: "owner_id",
                table: "contacts",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldCollation: "C");

            migrationBuilder.AlterColumn<string>(
                name: "chat_id",
                table: "contacts",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldCollation: "C");

            migrationBuilder.AlterColumn<string>(
                name: "id",
                table: "contacts",
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
