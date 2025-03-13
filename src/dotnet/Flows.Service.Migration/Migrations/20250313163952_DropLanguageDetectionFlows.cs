using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Flows.Migrations
{
    /// <inheritdoc />
    public partial class DropLanguageDetectionFlows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
            => migrationBuilder.Sql("""
                                    delete from _flows
                                    where id like 'LanguageDetect%'
                                    """);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("");
    }
}
