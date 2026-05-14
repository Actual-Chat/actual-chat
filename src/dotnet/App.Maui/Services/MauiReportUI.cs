using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;
using ActualLab.IO;
using Sentry;

namespace ActualChat.App.Maui.Services;

public sealed class MauiReportUI(IServiceProvider services) : ReportUI
{
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(15);

    private ILogger Log { get; } = services.LogFor<MauiReportUI>();
    private AccountUI AccountUI => field ??= services.GetRequiredService<AccountUI>();
    private HostInfo HostInfo => field ??= services.HostInfo();

    public override bool IsAvailable => true;

    public override async Task Submit(string comment, FilePath logFile, CancellationToken cancellationToken)
    {
        var account = AccountUI.OwnAccount.Value;
        var name = account.Name.NullIfEmpty() ?? "(no name)";
        var email = account.Email.NullIfEmpty() ?? "noreply@actual.chat";
        // TODO: just queue the report and show a notification
        if (!SentrySdk.IsEnabled)
            throw StandardError.Constraint("Diagnostics aren't ready yet — please retry in a few seconds.");

        var bytes = await File.ReadAllBytesAsync(logFile, cancellationToken).ConfigureAwait(false);
        var eventId = SentrySdk.CaptureMessage($"User report: {comment}",
            scope => {
                scope.User = new SentryUser {
                    Id = account.Id.Value,
                    Username = account.Name.NullIfEmpty(),
                    Email = account.Email.NullIfEmpty(),
                };
                scope.SetTag("app.kind", HostInfo.AppKind.ToString());
                scope.SetTag("host.kind", HostInfo.HostKind.ToString());
                scope.SetExtra("base.url", HostInfo.BaseUrl);
                scope.AddAttachment(bytes, logFile.FileName, AttachmentType.Default, "text/plain");
            });

        var feedback = new SentryFeedback(
            comment,
            contactEmail: email,
            name: name,
            associatedEventId: eventId);
        SentrySdk.CaptureFeedback(feedback);
        await SentrySdk.FlushAsync(FlushTimeout).ConfigureAwait(false);
    }
}
