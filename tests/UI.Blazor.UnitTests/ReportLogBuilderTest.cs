using System.Text;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;
using Microsoft.Extensions.Hosting;

namespace ActualChat.UI.Blazor.UnitTests;

public class ReportLogBuilderTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Moment GeneratedAt = new(new DateTime(2026, 5, 13, 10, 34, 0, DateTimeKind.Utc));

    [Fact]
    public void HeaderShouldIncludeAppEnvironmentAndCounts()
    {
        // arrange
        var hostInfo = NewHostInfo();
        var account = NewAccount();
        var entries = new[] {
            NewEntry(1, LogLevel.Information, "MyCategory", "hello"),
        };

        // act
        var text = BuildText(entries, hostInfo, account);

        // assert
        text.Should().StartWith("=== Voxt log report ===\n");
        text.Should().Contain("Generated:    2026-05-13T10:34:00.000Z\n");
        text.Should().Contain("HostKind=MauiApp");
        text.Should().Contain("AppKind=Android");
        text.Should().Contain("Environment=Production");
        text.Should().Contain("DeviceModel=Pixel 7");
        text.Should().Contain("Account:      ");
        text.Should().Contain(account!.Id.Value);
        text.Should().Contain(account.Name);
        text.Should().Contain("Entries:      1\n");
    }

    [Fact]
    public void HeaderShouldHandleNullAccount()
    {
        // arrange
        var hostInfo = NewHostInfo();

        // act
        var text = BuildText([], hostInfo, null);

        // assert
        text.Should().Contain("Account:      (none)");
        text.Should().Contain("Entries:      0\n");
    }

    [Fact]
    public void EntriesShouldBeFormattedWithTimestampLevelCategoryAndMessage()
    {
        // arrange
        var entries = new[] {
            NewEntry(1, LogLevel.Information, "A.B", "info-msg"),
            NewEntry(2, LogLevel.Warning, "C", "warn-msg"),
            NewEntry(3, LogLevel.Error, "D", "err-msg"),
        };

        // act
        var text = BuildText(entries, NewHostInfo(), null);

        // assert
        text.Should().Contain("[2026-05-13T10:00:00.000Z] INFO  A.B: info-msg\n");
        text.Should().Contain("[2026-05-13T10:00:00.000Z] WARN  C: warn-msg\n");
        text.Should().Contain("[2026-05-13T10:00:00.000Z] ERROR D: err-msg\n");
    }

    [Fact]
    public void ExceptionShouldBeIncludedWithIndentation()
    {
        // arrange
        Exception ex;
        try {
            throw new InvalidOperationException("boom");
        }
        catch (Exception e) {
            ex = e;
        }
        var entries = new[] {
            new LogEntry(1, "Cat", LogLevel.Error, default, "failed", ex,
                new Moment(new DateTime(2026, 5, 13, 10, 0, 0, DateTimeKind.Utc))),
        };

        // act
        var text = BuildText(entries, NewHostInfo(), null);

        // assert
        text.Should().Contain("ERROR Cat: failed\n");
        text.Should().Contain("  System.InvalidOperationException: boom");
        text.Should().Contain("  ");
    }

    [Fact]
    public void OutputShouldBeUtf8Bytes()
    {
        // arrange
        var entries = new[] {
            NewEntry(1, LogLevel.Information, "Cat", "héllo Ω"),
        };

        // act
        var bytes = ReportLogBuilder.BuildLogPayload(entries, NewHostInfo(), null, GeneratedAt);

        // assert
        var text = Encoding.UTF8.GetString(bytes);
        text.Should().Contain("héllo Ω");
    }

    // Private methods

    private static string BuildText(IReadOnlyList<LogEntry> entries, HostInfo hostInfo, AccountFull? account)
        => Encoding.UTF8.GetString(ReportLogBuilder.BuildLogPayload(entries, hostInfo, account, GeneratedAt));

    private static HostInfo NewHostInfo()
        => new() {
            HostKind = HostKind.MauiApp,
            AppKind = AppKind.Android,
            Environment = Environments.Production,
            BaseUrl = "https://voxt.example/",
            DeviceModel = "Pixel 7",
        };

    private static AccountFull NewAccount()
        => new(UserId.New(), 1) { Name = "Alice" };

    private static LogEntry NewEntry(long id, LogLevel level, string category, string message)
        => new(id, category, level, default, message, null,
            new Moment(new DateTime(2026, 5, 13, 10, 0, 0, DateTimeKind.Utc)));
}
