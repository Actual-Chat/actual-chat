using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Contacts.Migrations
{
    /// <inheritdoc />
    public partial class Places : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "place_id",
                table: "contacts",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "place_contacts",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    owner_id = table.Column<string>(type: "text", nullable: false),
                    place_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_place_contacts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_place_contacts_owner_id",
                table: "place_contacts",
                column: "owner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "place_contacts");

            migrationBuilder.DropColumn(
                name: "place_id",
                table: "contacts");
        }
    }
}
