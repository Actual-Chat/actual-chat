﻿using System;
using System.IO;

namespace ActualChat.Audio.Ebml.Models
{
    /// <summary>
    /// http://matroska.sourceforge.net/technical/specs/index.html#simpleblock_structure
    /// </summary>
    public sealed class SimpleBlock : Block
    {
        private const byte KeyFrameBit = 0b10000000;
        private const byte DiscardableBit = 0b00000001;
        /// <summary>
        /// Keyframe, set when the Block contains only keyframes
        /// </summary>
        public bool IsKeyFrame { get; private set; }

        /// <summary>
        /// Discardable, the frames of the Block can be discarded during playing if needed
        /// </summary>
        public bool IsDiscardable { get; private set; }

        public override void Parse(ReadOnlySpan<byte> span)
        {
            base.Parse(span);

            IsKeyFrame = (Flags & KeyFrameBit) == KeyFrameBit;
            IsDiscardable = (Flags & DiscardableBit) == DiscardableBit;
        }
        
        public override bool Write(ref SpanWriter writer)
        {
            if (!EbmlHelper.WriteEbmlMasterElement(MatroskaSpecification.SimpleBlock, GetSize(), ref writer))
                return false;
            
            writer.Write(VInt.EncodeSize(TrackNumber));
            writer.Write(TimeCode);
            writer.Write(Flags);
            writer.Write(Data);
            return true;
        }
    }
}