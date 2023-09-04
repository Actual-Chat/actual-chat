using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                                 insert into user_identities(id, user_id, secret)
                                 select 'email/' || lower(claims_json::json ->> 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress'),
                                        id,
                                        ''
                                 from users
                                 where nullif(claims_json::json ->> 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress', '')  is not null;
                                 """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                                 delete from user_identities
                                 where id like 'email/%';
                                 """);
        }
    }
}
