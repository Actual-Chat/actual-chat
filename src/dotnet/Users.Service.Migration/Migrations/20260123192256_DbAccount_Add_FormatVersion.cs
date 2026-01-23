using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    /// <inheritdoc />
    public partial class DbAccount_Add_FormatVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "claims_json",
                table: "accounts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "format_version",
                table: "accounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "account_identities",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    account_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    secret = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_identities", x => x.id);
                    table.ForeignKey(
                        name: "fk_account_identities_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_identities_account_id",
                table: "account_identities",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_identities_id",
                table: "account_identities",
                column: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_identities");

            migrationBuilder.DropColumn(
                name: "claims_json",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "format_version",
                table: "accounts");
        }
    }
}
