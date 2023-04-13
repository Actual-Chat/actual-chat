using ActualChat.Hosting;
using ActualChat.Users.UI.Blazor.Components;

namespace ActualChat.Users.UI.Blazor.Module;

public class UsersBlazorUIMatchingTypeRegistry : IMatchingTypeRegistry
{
    public Dictionary<(Type Source, Symbol Scope), Type> GetMatchedTypes()
        => new () {
            {(typeof(OwnAccountEditorModal.Model), typeof(IModalView).ToSymbol()), typeof(OwnAccountEditorModal)},
            {(typeof(OwnAvatarEditorModal.Model), typeof(IModalView).ToSymbol()), typeof(OwnAvatarEditorModal)},
        };
}
