using System.Security.Cryptography;
using System.Text;

namespace ActualChat.Rpc;

/// <summary>
/// Keeps one file per network in a folder. A network id can be an SSID or an address, so only its
/// salted hash reaches the file system. The least recently used files go once there are too many.
/// </summary>
public sealed class FileRpcPathStore(string folder, int maxNetworkCount = FileRpcPathStore.DefaultMaxNetworkCount)
    : IRpcPathStore
{
    public const int DefaultMaxNetworkCount = 20;
    private const string Extension = ".rpcpath";
    private const string Salt = "rpc-path/1/";

    private readonly Lock _lock = new();

    public string Folder { get; } = folder;

    public ValueTask<byte[]?> Load(string networkId, CancellationToken cancellationToken)
    {
        var path = GetPath(networkId);
        lock (_lock) {
            if (!File.Exists(path))
                return ValueTask.FromResult<byte[]?>(null);

            var data = File.ReadAllBytes(path);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            return ValueTask.FromResult<byte[]?>(data);
        }
    }

    public ValueTask Save(string networkId, byte[] data, CancellationToken cancellationToken)
    {
        var path = GetPath(networkId);
        lock (_lock) {
            Directory.CreateDirectory(Folder);
            // Written aside and moved over, so a crash leaves the old file rather than half of a new one
            var tempPath = path + ".tmp";
            File.WriteAllBytes(tempPath, data);
            File.Move(tempPath, path, overwrite: true);
            Prune(path);
        }
        return ValueTask.CompletedTask;
    }

    // Private methods

    private string GetPath(string networkId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Salt + networkId));
        return Path.Combine(Folder, Convert.ToHexString(hash, 0, 12).ToLowerInvariant() + Extension);
    }

    private void Prune(string keepPath)
    {
        var files = new DirectoryInfo(Folder)
            .EnumerateFiles("*" + Extension)
            .Where(x => !string.Equals(x.FullName, keepPath, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.LastWriteTimeUtc)
            .ToList();
        foreach (var file in files.Skip(Math.Max(0, maxNetworkCount - 1)))
            try {
                file.Delete();
            }
            catch (IOException) {
                // It goes next time
            }
    }
}
