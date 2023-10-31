using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Contacts.Migrations
{
    /// <inheritdoc />
    public partial class ExternalContactLinks_IsChecked : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_checked",
                table: "external_contact_links",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_external_contact_links_is_checked",
                table: "external_contact_links",
                column: "is_checked");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_external_contact_links_is_checked",
                table: "external_contact_links");

            migrationBuilder.DropColumn(
                name: "is_checked",
                table: "external_contact_links");
        }
    }
}
