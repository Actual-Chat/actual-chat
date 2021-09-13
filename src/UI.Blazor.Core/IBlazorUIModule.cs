using System;

namespace ActualChat.UI.Blazor
{
    public interface IBlazorUIModule
    {
        public string[] CssPaths => Array.Empty<string>();
        public string[] ScriptPaths => Array.Empty<string>();
    }
}
