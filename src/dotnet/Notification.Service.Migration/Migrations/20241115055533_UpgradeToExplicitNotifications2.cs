using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Notification.Migrations
{
    /// <inheritdoc />
    public partial class UpgradeToExplicitNotifications2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_manual_notifications",
                table: "manual_notifications");

            migrationBuilder.RenameTable(
                name: "manual_notifications",
                newName: "explicit_notifications");

            migrationBuilder.RenameIndex(
                name: "ix_manual_notifications_user_id_version",
                table: "explicit_notifications",
                newName: "ix_explicit_notifications_user_id_version");

            migrationBuilder.RenameIndex(
                name: "ix_manual_notifications_user_id_kind_similarity_key",
                table: "explicit_notifications",
                newName: "ix_explicit_notifications_user_id_kind_similarity_key");

            migrationBuilder.RenameIndex(
                name: "ix_manual_notifications_user_id_id",
                table: "explicit_notifications",
                newName: "ix_explicit_notifications_user_id_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_explicit_notifications",
                table: "explicit_notifications",
                column: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_explicit_notifications",
                table: "explicit_notifications");

            migrationBuilder.RenameTable(
                name: "explicit_notifications",
                newName: "manual_notifications");

            migrationBuilder.RenameIndex(
                name: "ix_explicit_notifications_user_id_version",
                table: "manual_notifications",
                newName: "ix_manual_notifications_user_id_version");

            migrationBuilder.RenameIndex(
                name: "ix_explicit_notifications_user_id_kind_similarity_key",
                table: "manual_notifications",
                newName: "ix_manual_notifications_user_id_kind_similarity_key");

            migrationBuilder.RenameIndex(
                name: "ix_explicit_notifications_user_id_id",
                table: "manual_notifications",
                newName: "ix_manual_notifications_user_id_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_manual_notifications",
                table: "manual_notifications",
                column: "id");
        }
    }
}
