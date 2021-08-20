using System;
using ActualChat.Audio.WebM.Models;

namespace ActualChat.Audio.WebM
{
    public ref struct WebMWriter
    {
        private SpanWriter _spanWriter;
        

        public WebMWriter(Span<byte> span)
        {
            _spanWriter = new SpanWriter(span);
        }

        public ReadOnlySpan<byte> Written => _spanWriter.Span[.._spanWriter.Position];

        public int Position => _spanWriter.Position; 

        public bool Write(BaseModel entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            
            var beforePosition = _spanWriter.Position;
            var success = entry.Write(ref _spanWriter);
            if (success) return true;
            
            _spanWriter.Position = beforePosition;
            return false;

        }

    }
}