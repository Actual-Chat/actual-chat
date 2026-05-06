using ActualChat.Security;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Chat.Controllers;

[ApiController, Route("api/audio-blob")]
public sealed class AudioBlobController(IServiceProvider services) : ControllerBase
{
    private const string DefaultContentType = "audio/webm";

    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IBlobStorages BlobStorages { get; } = services.GetRequiredService<IBlobStorages>();

    [HttpGet("{chatSid}/{localId:long}")]
    public async Task<IActionResult> DownloadByEntry(string chatSid, long localId, CancellationToken cancellationToken)
    {
        var session = HttpContext.TryGetSessionFromHeader(SessionFormat.Token)
            ?? HttpContext.GetSessionFromCookie();
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeAdmin);

        if (!ChatId.TryParse(chatSid, out var chatId))
            return BadRequest($"Invalid ChatId: '{chatSid}'");

        var entryId = ChatEntryId.New(chatId, localId);
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat is null)
            return NotFound($"Chat not found or you have no access: {chatId}");

        var entry = await Chats.GetEntry(session, entryId, cancellationToken).ConfigureAwait(false);
        if (entry is null)
            return NotFound($"Chat entry not found: {entryId}");

        var blobId = entry.Audio?.BlobId;
        if (blobId.IsNullOrEmpty())
            return NotFound($"Chat entry {entryId} has no audio blob");

        var blobStorage = BlobStorages[BlobScope.AudioRecord];
        var stream = await blobStorage.Read(blobId, cancellationToken).ConfigureAwait(false);
        if (stream is null)
            return NotFound($"Audio blob '{blobId}' not found in storage");

        var contentType = await blobStorage.GetContentType(blobId, cancellationToken).ConfigureAwait(false);
        var extension = Path.GetExtension(blobId);
        if (extension.IsNullOrEmpty())
            extension = ".webm";
        var fileName = $"{chatId.Value}_{localId.Format()}{extension}";
        return File(stream, contentType ?? DefaultContentType, fileName, enableRangeProcessing: true);
    }
}
