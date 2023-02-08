using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    /// <inheritdoc />
    public partial class UpdateAccountWithClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                update accounts
                set
                    name = subquery.first_name,
                    last_name = subquery.last_name,
                    email = subquery.email
                from (
                    select
                        id,
                        claims_json::json->>'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/givenname' AS first_name,
                        claims_json::json->>'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/surname' AS last_name,
                        claims_json::json->>'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress' AS email
                    from users
                    where claims_json <> '{}'
                ) as subquery
                where accounts.id = subquery.id;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
