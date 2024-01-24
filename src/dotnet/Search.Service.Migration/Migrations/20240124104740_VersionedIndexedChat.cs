using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Search.Migrations
{
    /// <inheritdoc />
    public partial class VersionedIndexedChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                                 update indexed_chat
                                 set id = 'v0-' || id
                                 where id not like 'v_-%';
                                 """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                                 update indexed_chat
                                 set id = substring(id from 4)
                                 where id like 'v0-%';
                                 """);
        }
    }
}
