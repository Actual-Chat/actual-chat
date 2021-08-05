using Stl.DependencyInjection;

namespace ActualChat.Storage
{
    [RegisterSettings("ActualChat.Storage")]
    public class StorageSettings
    {
        public string StorageType { get; set; } = "disk"; // ? @AY, can we use enum here? 
        public string LocalStoragePath { get; set; } = string.Empty;
    }
}
