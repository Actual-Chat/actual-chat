using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Contacts.Migrations
{
    /// <inheritdoc />
    public partial class ExternalContacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "external_contacts",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    given_name = table.Column<string>(type: "text", nullable: false),
                    family_name = table.Column<string>(type: "text", nullable: false),
                    middle_name = table.Column<string>(type: "text", nullable: false),
                    name_prefix = table.Column<string>(type: "text", nullable: false),
                    name_suffix = table.Column<string>(type: "text", nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_contacts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "external_emails",
                columns: table => new
                {
                    external_contact_id = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_emails", x => new { x.external_contact_id, x.email });
                    table.ForeignKey(
                        name: "fk_external_emails_external_contacts_external_contact_id",
                        column: x => x.external_contact_id,
                        principalTable: "external_contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "external_phones",
                columns: table => new
                {
                    external_contact_id = table.Column<string>(type: "text", nullable: false),
                    phone = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_phones", x => new { x.external_contact_id, x.phone });
                    table.ForeignKey(
                        name: "fk_external_phones_external_contacts_external_contact_id",
                        column: x => x.external_contact_id,
                        principalTable: "external_contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_external_emails_email",
                table: "external_emails",
                column: "email");

            migrationBuilder.CreateIndex(
                name: "ix_external_emails_external_contact_id",
                table: "external_emails",
                column: "external_contact_id");

            migrationBuilder.CreateIndex(
                name: "ix_external_phones_external_contact_id",
                table: "external_phones",
                column: "external_contact_id");

            migrationBuilder.CreateIndex(
                name: "ix_external_phones_phone",
                table: "external_phones",
                column: "phone");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "external_emails");

            migrationBuilder.DropTable(
                name: "external_phones");

            migrationBuilder.DropTable(
                name: "external_contacts");
        }
    }
}
