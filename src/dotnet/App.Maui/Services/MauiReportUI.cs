using ActualChat.UI.Blazor.Services;
using ActualLab.IO;
using Sentry;

namespace ActualChat.App.Maui.Services;

public sealed class MauiReportUI(IServiceProvider services) : ReportUI
{
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(15);

    private ILogger Log { get; } = services.LogFor<MauiReportUI>();
    private AccountUI AccountUI => field ??= services.GetRequiredService<AccountUI>();

    public override bool IsAvailable => true;

    public override async Task Submit(string comment, FilePath logFile, CancellationToken cancellationToken)
    {
        var account = AccountUI.OwnAccount.Value;
        var hasAccount = account != null && account.HasId();
        var name = hasAccount ? account!.Name.NullIfEmpty() ?? "(no name)" : "(no account)";
        var email = hasAccount ? account!.Email.NullIfEmpty() ?? "noreply@actual.chat" : "noreply@actual.chat";
        Log.LogInformation(
            "Submit: comment.Length={Length}, logFile={LogFile}, SentryEnabled={SentryEnabled}, name={Name}, email={Email}",
            comment.Length, logFile.Value, SentrySdk.IsEnabled, name, email);
        if (!SentrySdk.IsEnabled)
            throw StandardError.Constraint("Diagnostics aren't ready yet — please retry in a few seconds.");

        var bytes = await File.ReadAllBytesAsync(logFile, cancellationToken).ConfigureAwait(false);
        Log.LogInformation("Submit: read {ByteCount} bytes from log file", bytes.Length);

        var eventId = SentrySdk.CaptureMessage($"User report: {comment}", scope => {
            scope.AddAttachment(bytes, logFile.FileName, AttachmentType.Default, "text/plain");
        }, SentryLevel.Info);
        Log.LogInformation("Submit: CaptureMessage returned EventId={EventId}", eventId);

        var feedback = new SentryFeedback(
            comment,
            contactEmail: email,
            name: name,
            associatedEventId: eventId);
        SentrySdk.CaptureFeedback(feedback);
        Log.LogInformation("Submit: CaptureFeedback called");

        await SentrySdk.FlushAsync(FlushTimeout).ConfigureAwait(false);
        Log.LogInformation("Submit: FlushAsync done");
    }
}
