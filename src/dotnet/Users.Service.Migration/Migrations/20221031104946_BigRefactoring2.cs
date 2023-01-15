using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    public partial class BigRefactoring2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_user_presences",
                table: "user_presences");

            migrationBuilder.DropPrimaryKey(
                name: "pk_user_contacts",
                table: "user_contacts");

            migrationBuilder.DropPrimaryKey(
                name: "pk_user_avatars",
                table: "user_avatars");

            migrationBuilder.DropPrimaryKey(
                name: "pk_chat_read_positions",
                table: "chat_read_positions");

            migrationBuilder.RenameTable(
                name: "user_presences",
                newName: "presences");

            migrationBuilder.RenameTable(
                name: "user_contacts",
                newName: "contacts");

            migrationBuilder.RenameTable(
                name: "user_avatars",
                newName: "avatars");

            migrationBuilder.RenameTable(
                name: "chat_read_positions",
                newName: "read_positions");

            migrationBuilder.RenameIndex(
                name: "ix_user_contacts_owner_user_id",
                table: "contacts",
                newName: "ix_contacts_owner_user_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_presences",
                table: "presences",
                column: "user_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_contacts",
                table: "contacts",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_avatars",
                table: "avatars",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_read_positions",
                table: "read_positions",
                column: "id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_read_positions",
                table: "read_positions");

            migrationBuilder.DropPrimaryKey(
                name: "pk_presences",
                table: "presences");

            migrationBuilder.DropPrimaryKey(
                name: "pk_contacts",
                table: "contacts");

            migrationBuilder.DropPrimaryKey(
                name: "pk_avatars",
                table: "avatars");

            migrationBuilder.RenameTable(
                name: "read_positions",
                newName: "chat_read_positions");

            migrationBuilder.RenameTable(
                name: "presences",
                newName: "user_presences");

            migrationBuilder.RenameTable(
                name: "contacts",
                newName: "user_contacts");

            migrationBuilder.RenameTable(
                name: "avatars",
                newName: "user_avatars");

            migrationBuilder.RenameIndex(
                name: "ix_contacts_owner_user_id",
                table: "user_contacts",
                newName: "ix_user_contacts_owner_user_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_chat_read_positions",
                table: "chat_read_positions",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_presences",
                table: "user_presences",
                column: "user_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_contacts",
                table: "user_contacts",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_avatars",
                table: "user_avatars",
                column: "id");
        }
    }
}
