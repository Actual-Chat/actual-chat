using System;
using Microsoft.AspNetCore.Components;

namespace ActualChat.Host
{
    public class HostUriProvider : IHostUriProvider
    {
        private readonly NavigationManager _navigationManager;

        public HostUriProvider(NavigationManager navigationManager)
        {
            _navigationManager = navigationManager;
            BaseUri = _navigationManager.BaseUri;
            Uri = _navigationManager.Uri;
        }

        public string BaseUri { get; }
        public string Uri { get; }
        
        public Uri GetAbsoluteUri(string relativeUri) => _navigationManager.ToAbsoluteUri(relativeUri);

        public string GetBaseRelativePath(string uri) => _navigationManager.ToBaseRelativePath(uri);
    }
}