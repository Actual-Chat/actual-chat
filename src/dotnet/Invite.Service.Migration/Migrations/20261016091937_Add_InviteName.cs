using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Invite.Migrations;

/// <inheritdoc />
public partial class _20261016091937_Add_InviteName : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "name",
            table: "invites",
            type: "text",
            nullable: false,
            defaultValue: "");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "name",
            table: "invites");
    }
}
