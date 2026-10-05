using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Invite.Migrations;

/// <inheritdoc />
public partial class _20261005020122_Fusion15_DeferredInvalidation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "command_json",
            table: "_operations");

        migrationBuilder.DropColumn(
            name: "items_json",
            table: "_operations");

        migrationBuilder.DropColumn(
            name: "nested_operations",
            table: "_operations");

        migrationBuilder.AddColumn<byte[]>(
            name: "command_data",
            table: "_operations",
            type: "bytea",
            nullable: true);

        migrationBuilder.AddColumn<byte[]>(
            name: "invalidation_calls_data",
            table: "_operations",
            type: "bytea",
            nullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "value_json",
            table: "_events",
            type: "text",
            nullable: true,
            oldClrType: typeof(string),
            oldType: "text");

        migrationBuilder.AddColumn<byte[]>(
            name: "value_data",
            table: "_events",
            type: "bytea",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "command_data",
            table: "_operations");

        migrationBuilder.DropColumn(
            name: "invalidation_calls_data",
            table: "_operations");

        migrationBuilder.DropColumn(
            name: "value_data",
            table: "_events");

        migrationBuilder.AddColumn<string>(
            name: "command_json",
            table: "_operations",
            type: "text",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "items_json",
            table: "_operations",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "nested_operations",
            table: "_operations",
            type: "text",
            nullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "value_json",
            table: "_events",
            type: "text",
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "text",
            oldNullable: true);
    }
}
