using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Media.Migrations
{
    /// <inheritdoc />
    public partial class ChatEntry_Refactoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "content_id",
                table: "media",
                newName: "blob_id");

            migrationBuilder.RenameIndex(
                name: "ix_media_content_id",
                table: "media",
                newName: "ix_media_blob_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "blob_id",
                table: "media",
                newName: "content_id");

            migrationBuilder.RenameIndex(
                name: "ix_media_blob_id",
                table: "media",
                newName: "ix_media_content_id");
        }
    }
}
