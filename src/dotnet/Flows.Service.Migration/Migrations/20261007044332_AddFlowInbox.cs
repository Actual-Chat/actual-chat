using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Flows.Migrations;

/// <inheritdoc />
public partial class _20261007044332_AddFlowInbox : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "_flow_inboxes",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                version = table.Column<long>(type: "bigint", nullable: false),
                last_id = table.Column<long>(type: "bigint", nullable: false),
                data = table.Column<byte[]>(type: "bytea", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_flow_inboxes", x => x.id);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "_flow_inboxes");
    }
}
