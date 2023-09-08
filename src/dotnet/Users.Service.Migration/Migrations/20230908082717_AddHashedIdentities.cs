using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    /// <inheritdoc />
    public partial class AddHashedIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                                 insert into user_identities(id, user_id, secret)
                                 select 'phone-hash/' || encode(sha256(substring(id from 7)::bytea), 'hex'), user_id, secret
                                 from user_identities
                                 where id like 'phone/%';

                                 insert into user_identities(id, user_id, secret)
                                 select 'email-hash/' || upper(encode(sha256(substring(id from 7)::bytea), 'hex')), user_id, secret
                                 from user_identities
                                 where id like 'email/%';
                                 """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                                 delete from user_identities
                                 where id like 'phone-hash/%';

                                 delete from user_identities
                                 where id like 'email-hash/%';
                                 """);
        }
    }
}
