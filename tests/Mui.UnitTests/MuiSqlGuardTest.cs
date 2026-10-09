namespace ActualChat.Mui.UnitTests;

public class MuiSqlGuardTest
{
    [Theory]
    [InlineData("select 1", "select 1")]
    [InlineData("  select 1;  ", "select 1")]
    [InlineData("select 1; -- done", "select 1")]
    [InlineData("select 1; /* done */", "select 1")]
    [InlineData("-- head\nselect 1", "-- head\nselect 1")]
    [InlineData("select ';' as semi", "select ';' as semi")]
    [InlineData("select 'it''s; ok'", "select 'it''s; ok'")]
    [InlineData("select \"a;b\" from t", "select \"a;b\" from t")]
    [InlineData("select $$a;b$$", "select $$a;b$$")]
    [InlineData("select $q$a;b$q$ as x;", "select $q$a;b$q$ as x")]
    [InlineData("select /* a; b */ 1", "select /* a; b */ 1")]
    [InlineData("select /* a /* nested; */ b; */ 1", "select /* a /* nested; */ b; */ 1")]
    [InlineData("select E'a\\';b'", "select E'a\\';b'")]
    [InlineData("select $1", "select $1")]
    public void SingleStatementIsAccepted(string sql, string expected)
        => MuiSqlGuard.GetSingleStatement(sql).Should().Be(expected);

    [Theory]
    [InlineData("select 1; select 2")]
    [InlineData("select 1;; ")]
    [InlineData("select 1; drop table t")]
    [InlineData("select 1; -- c\nselect 2")]
    [InlineData("select 'a'; select 'b'")]
    [InlineData("select E'a\\'; delete from t; --'; select 1")]
    public void SeveralStatementsAreRejected(string sql)
    {
        // act
        var parse = () => MuiSqlGuard.GetSingleStatement(sql);

        // assert
        parse.Should().Throw<Exception>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(";")]
    [InlineData("-- only a comment")]
    [InlineData("/* only a comment */;")]
    public void EmptyStatementIsRejected(string sql)
    {
        // act
        var parse = () => MuiSqlGuard.GetSingleStatement(sql);

        // assert
        parse.Should().Throw<Exception>();
    }

    [Theory]
    [InlineData("select 'abc")]
    [InlineData("select \"abc")]
    [InlineData("select /* abc")]
    [InlineData("select $$abc")]
    public void UnterminatedLiteralIsRejected(string sql)
    {
        // act
        var parse = () => MuiSqlGuard.GetSingleStatement(sql);

        // assert
        parse.Should().Throw<Exception>();
    }
}
