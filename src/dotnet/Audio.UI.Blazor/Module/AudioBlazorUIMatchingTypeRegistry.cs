using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.Hosting;

namespace ActualChat.Audio.UI.Blazor.Module;

public class AudioBlazorUIMatchingTypeRegistry : IMatchingTypeRegistry
{
    public Dictionary<(Type Source, Symbol Scope), Type> GetMatchedTypes()
        => new () {
            {(typeof(RecordingPermissionModal.Model), typeof(IModalView).ToSymbol()), typeof(RecordingPermissionModal)},
        };
}
