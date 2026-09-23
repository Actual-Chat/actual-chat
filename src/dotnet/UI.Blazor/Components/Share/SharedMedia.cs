namespace ActualChat.UI.Blazor.Components;

/// <summary>
/// Media attached to a <see cref="ShareRequest"/>: the reference used to re-post it
/// inside Voxt plus what's needed to hand the file over to an external app.
/// </summary>
public sealed record SharedMedia(MediaRef Ref, string FileName, string ContentType)
{
    public static SharedMedia New(ChatEntryAttachment attachment)
        => new(attachment.ToMediaRef(), attachment.Media.FileName, attachment.Media.ContentType);

    public string GetFileName()
        => FileName.NullIfEmpty() ?? "media" + (MediaTypeExt.GetFileExtension(ContentType) ?? "");
}
