using System;
using Microsoft.AspNetCore.Components;

namespace ActualChat.UI.Blazor
{
    public class HostUriMapper : IHostUriMapper
    {
        public Uri BaseUri { get; }

        public HostUriMapper(NavigationManager nav)
            => BaseUri = new Uri(nav.BaseUri);
    }
}
