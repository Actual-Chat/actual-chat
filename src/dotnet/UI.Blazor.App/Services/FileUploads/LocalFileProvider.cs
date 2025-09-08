// using MemoryPack;
//
// namespace ActualChat.UI.Blazor.App.Services;
//
// [DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// public partial class LocalFileProvider : IFileProvider
// {
//     [DataMember, MemoryPackOrder(0)]
//     public string FilePath { get; init; } = "";
//     [field: AllowNull, MaybeNull]
//     private FileInfo FileInfo => field ??= new FileInfo(FilePath);
//
//     [IgnoreDataMember, MemoryPackIgnore]
//     public long FileSize => FileInfo.Length;
//     [IgnoreDataMember, MemoryPackIgnore]
//     public string FileName => FileInfo.Name;
//
//     public async Task<bool> CheckAccess()
//     {
//         try {
//             var stream = await OpenReadAsync(0).ConfigureAwait(false);
//             await using (stream.ConfigureAwait(false)) { }
//             return true;
//         }
//         catch {
//             Console.WriteLine($"Файл для сессии {FileName} недоступен");
//             return false;
//         }
//     }
//
//     public Task PrepareForSaving()
//         => throw new NotImplementedException();
//
//     public IFileUploadOperation? CreateUploadOperation()
//         => throw new NotImplementedException();
//
//     private Task<Stream> OpenReadAsync(long offset = 0)
//     {
//         var stream = FileInfo.OpenRead();
//         stream.Seek(offset, SeekOrigin.Begin);
//         return Task.FromResult<Stream>(stream);
//     }
// }
