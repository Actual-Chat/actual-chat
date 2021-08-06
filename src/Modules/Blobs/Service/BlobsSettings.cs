using Stl.DependencyInjection;

namespace ActualChat.Blobs
{
    [RegisterSettings("ActualChat.Blobs")]
    public class BlobsSettings
    {
        // DBs
        public string Db { get; set; } =
            "Server=localhost;Database=ac_dev_blobs;Port=5432;User Id=postgres;Password=ActualChat.Dev.2021.07";
    }
}
