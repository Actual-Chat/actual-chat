using MemoryPack;

namespace ActualChat.UI.Blazor.App.Services;

[MemoryPackUnion(0, typeof(LocalFileProvider))]
public partial interface IFileProvider
{
    [IgnoreDataMember, MemoryPackIgnore]
    string FileName { get; }
    [IgnoreDataMember, MemoryPackIgnore]
    long FileSize { get; }
    Task<Stream> OpenReadAsync(long offset = 0);
}
