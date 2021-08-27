using System;

namespace ActualChat
{
    public interface IHostUriProvider
    {
        string BaseUri { get; }
        
        string Uri { get; }

        Uri GetAbsoluteUri(string relativeUri);

        string GetBaseRelativePath(string uri);
    }
}