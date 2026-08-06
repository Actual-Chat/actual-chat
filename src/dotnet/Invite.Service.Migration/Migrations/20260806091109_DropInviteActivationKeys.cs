using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Invite.Migrations;

/// <inheritdoc />
public partial class DropInviteActivationKeys : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "invite_activation_keys");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "invite_activation_keys",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false, collation: "C")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_invite_activation_keys", x => x.id);
            });
    }
}
