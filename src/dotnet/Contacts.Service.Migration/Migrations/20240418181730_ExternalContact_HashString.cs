using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Contacts.Migrations
{
    /// <inheritdoc />
    public partial class ExternalContact_HashString : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "sha256_hash",
                table: "external_contacts_hashes",
                newName: "hash");

            migrationBuilder.RenameColumn(
                name: "sha256_hash",
                table: "external_contacts",
                newName: "hash");

            migrationBuilder.Sql("""
                                 delete from external_contacts_hashes
                                 where hash not like '3 1 %';
                                 """);

            migrationBuilder.Sql("""
                                 update external_contacts
                                 set hash = ''
                                 where hash not like '3 1 %';
                                 """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "hash",
                table: "external_contacts_hashes",
                newName: "sha256_hash");

            migrationBuilder.RenameColumn(
                name: "hash",
                table: "external_contacts",
                newName: "sha256_hash");
        }
    }
}
