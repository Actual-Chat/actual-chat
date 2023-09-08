using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Contacts.Migrations
{
    /// <inheritdoc />
    public partial class ExternalContactLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "external_emails");

            migrationBuilder.DropTable(
                name: "external_phones");

            migrationBuilder.CreateTable(
                name: "external_contact_links",
                columns: table => new
                {
                    external_contact_id = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_contact_links", x => new { x.external_contact_id, x.value });
                    table.ForeignKey(
                        name: "fk_external_contact_links_external_contacts_external_contact_id",
                        column: x => x.external_contact_id,
                        principalTable: "external_contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_external_contact_links_value",
                table: "external_contact_links",
                column: "value");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "external_contact_links");

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
    }
}
