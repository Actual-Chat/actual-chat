using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class Authors_IsPlaceAuthor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_place_author",
                table: "authors",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_authors_version_is_place_author",
                table: "authors",
                columns: new[] { "version", "is_place_author" });

            migrationBuilder.Sql("""
                                 update authors
                                 set is_place_author = true
                                 where chat_id ~ '^s-(\w+)-\1$';
                                 """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_authors_version_is_place_author",
                table: "authors");

            migrationBuilder.DropColumn(
                name: "is_place_author",
                table: "authors");
        }
    }
}
