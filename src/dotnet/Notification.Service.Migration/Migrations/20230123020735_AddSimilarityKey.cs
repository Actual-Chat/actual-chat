using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Notification.Migrations
{
    /// <inheritdoc />
    public partial class AddSimilarityKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Id format is changed, so it's easier to drop everything
            migrationBuilder.Sql("""
            delete from notifications
            """);

            migrationBuilder.AddColumn<DateTime>(
                name: "sent_at",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "similarity_key",
                table: "notifications",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_kind_similarity_key",
                table: "notifications",
                columns: new[] { "user_id", "kind", "similarity_key" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_version",
                table: "notifications",
                columns: new[] { "user_id", "version" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notifications_user_id_kind_similarity_key",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_notifications_user_id_version",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "sent_at",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "similarity_key",
                table: "notifications");
        }
    }
}
