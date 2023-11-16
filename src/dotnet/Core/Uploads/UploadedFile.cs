namespace ActualChat.Uploads;

public abstract record UploadedFile(string FileName, string ContentType)
{
    public abstract long Length { get; }
    public abstract Stream Open();

    public async Task<string> GetSHA256HashCode(HashEncoding hashEncoding)
    {
        var stream = Open();
        await using var _ = stream.ConfigureAwait(false);
        return await stream.GetSHA256HashCode(hashEncoding).ConfigureAwait(false);
    }
}
