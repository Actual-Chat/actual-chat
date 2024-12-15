using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.MLSearch.Migrations
{
    /// <inheritdoc />
    public partial class RemoveContactIndexState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contact_index_state");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contact_index_state",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    last_updated_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    last_updated_version = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact_index_state", x => x.id);
                });
        }
    }
}
