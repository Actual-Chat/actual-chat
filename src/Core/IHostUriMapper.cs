using System;

namespace ActualChat
{
    public interface IHostUriMapper
    {
        public Uri BaseUri { get; }

        public Uri GetAbsoluteUri(string relativeUri)
            => new(BaseUri, relativeUri);
    }
}
