using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    /// <inheritdoc />
    public partial class FixHashedIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // delete identies with incorrect phoane and email hashes
            migrationBuilder.Sql("""
                                 delete
                                 from user_identities
                                 where id like '%phone-hash/%'
                                   and substring(id from 12) <> (select replace(encode(sha256(substring(id from 7)::bytea), 'base64'), '/', '\/')
                                                                 from user_identities ui2
                                                                 where ui2.user_id = user_identities.user_id
                                                                   and id like 'phone/%'
                                                                 limit 1);

                                 delete
                                 from user_identities
                                 where id like '%email-hash/%'
                                   and substring(id from 12) <> (select replace(encode(sha256(substring(id from 7)::bytea), 'base64'), '/', '\/')
                                                                 from user_identities ui2
                                                                 where ui2.user_id = user_identities.user_id
                                                                   and id like 'email/%'
                                                                 limit 1);
                                 """);

            // generate missing hashed email and phone identities
            migrationBuilder.Sql("""
                                 insert into user_identities(id, user_id, secret)
                                 select 'phone-hash/' || (select replace(encode(sha256(substring(id from 7)::bytea), 'base64'), '/', '\/')
                                                          from user_identities ui2
                                                          where ui2.user_id = u.id
                                                            and id like 'phone/%'
                                                          limit 1),
                                        u.id,
                                        ''
                                 from users u
                                 where not exists(select 1
                                                  from user_identities ui2
                                                  where ui2.user_id = u.id
                                                    and id like 'phone-hash/%')
                                   and exists(select 1
                                              from user_identities ui2
                                              where ui2.user_id = u.id
                                                and id like 'phone/%');

                                 insert into user_identities(id, user_id, secret)
                                 select 'email-hash/' || (select replace(encode(sha256(substring(id from 7)::bytea), 'base64'), '/', '\/')
                                                          from user_identities ui2
                                                          where ui2.user_id = u.id
                                                            and id like 'email/%'
                                                          limit 1),
                                        u.id,
                                        ''
                                 from users u
                                 where not exists(select 1
                                                  from user_identities ui2
                                                  where ui2.user_id = u.id
                                                    and id like 'email-hash/%')
                                   and exists(select 1
                                              from user_identities ui2
                                              where ui2.user_id = u.id
                                                and id like 'email/%');
                                 """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
