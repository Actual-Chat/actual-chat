namespace ActualChat.Mui.UnitTests;

public class MuiTableInfoTest
{
    [Fact]
    public void ColumnsAreKeyFirstThenVersionThenAlphabetical()
    {
        // arrange
        var table = new MuiTableInfo("authors", [
            new MuiColumnInfo("name", "text", false, false),
            new MuiColumnInfo("version", "bigint", false, false),
            new MuiColumnInfo("id", "text", false, true),
            new MuiColumnInfo("avatar_id", "text", true, false),
            new MuiColumnInfo("chat_id", "text", false, true),
        ]);

        // act
        var names = table.GetOrderedColumns().Select(x => x.Name);

        // assert
        names.Should().Equal("chat_id", "id", "version", "avatar_id", "name");
    }

    [Fact]
    public void TableWithoutVersionHasNoGap()
    {
        // arrange
        var table = new MuiTableInfo("t", [
            new MuiColumnInfo("b", "text", false, false),
            new MuiColumnInfo("a", "text", false, false),
        ]);

        // act
        var names = table.GetOrderedColumns().Select(x => x.Name);

        // assert
        names.Should().Equal("a", "b");
    }
}
