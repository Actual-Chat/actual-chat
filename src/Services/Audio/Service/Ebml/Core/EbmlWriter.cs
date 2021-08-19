using System;
using System.IO;
using ActualChat.Audio.Ebml.Models;

namespace ActualChat.Audio.Ebml
{
    public ref struct EbmlWriter
    {
        private SpanWriter _spanWriter;
        private BaseModel _entry;
        

        public EbmlWriter(Span<byte> span)
        {
            _spanWriter = new SpanWriter(span);
            _entry = BaseModel.Empty;
        }

        public State GetState()
        {
            return new State(_entry);
        }
        
        public ReadOnlySpan<byte> Written => _spanWriter.Span[.._spanWriter.Position];

        public bool Write(BaseModel entry)
        {
            // TODO: capture writer position before and restore it in case of failure of underlying write attempt
            throw new NotImplementedException();
        }

        public bool Write(EBML ebml)
        {
            _entry = ebml;
            var beginPosition = _spanWriter.Position;
            _spanWriter.Write(MatroskaSpecification.EBMLDescriptor.Identifier);
            // _spanWriter.Write(VInt.GetSizeOf(31));
            _spanWriter.Write(MatroskaSpecification.EBMLVersionDescriptor.Identifier);
            // _spanWriter.Write(VInt.GetSizeOf(ebml.EBMLVersion));
            _spanWriter.Write(ebml.EBMLVersion);
            _spanWriter.Write(MatroskaSpecification.EBMLReadVersionDescriptor.Identifier);
            // _spanWriter.Write(VInt.GetSizeOf(ebml.EBMLReadVersion));
            _spanWriter.Write(ebml.EBMLReadVersion);
            /*00000  Header (5 bytes)
00000   Name:                                 172351395 (0xA45DFA3)
00004   Size:                                 31 (0x1F)
00005  EBMLVersion - 1 (0x1) (4 bytes)
00005   Header (3 bytes)
00005    Name:                                646 (0x0286)
00007    Size:                                1 (0x01)
00008   Data:                                 1 (0x01)
00009  EBMLReadVersion - 1 (0x1) (4 bytes)
00009   Header (3 bytes)
00009    Name:                                759 (0x02F7)
0000B    Size:                                1 (0x01)
0000C   Data:                                 1 (0x01)
0000D  EBMLMaxIDLength - 4 (0x4) (4 bytes)
0000D   Header (3 bytes)
0000D    Name:                                754 (0x02F2)
0000F    Size:                                1 (0x01)
00010   Data:                                 4 (0x04)
00011  EBMLMaxSizeLength - 8 (0x8) (4 bytes)
00011   Header (3 bytes)
00011    Name:                                755 (0x02F3)
00013    Size:                                1 (0x01)
00014   Data:                                 8 (0x08)
00015  DocType - webm (7 bytes)
00015   Header (3 bytes)
00015    Name:                                642 (0x0282)
00017    Size:                                4 (0x04)
00018   Data:                                 webm
0001C  ------------------------------
0001C  ---   Matroska, accepted   ---
0001C  ------------------------------
0001C  DocTypeVersion - 4 (0x4) (4 bytes)
0001C   Header (3 bytes)
0001C    Name:                                647 (0x0287)
0001E    Size:                                1 (0x01)
0001F   Data:                                 4 (0x04)
00020  DocTypeReadVersion - 2 (0x2) (4 bytes)
00020   Header (3 bytes)
00020    Name:                                645 (0x0285)
00022    Size:                                1 (0x01)
00023   Data:                                 2 (0x02)*/
            throw new NotImplementedException();
        }

        public bool Write(Segment segment)
        {
            _entry = segment;
            throw new NotImplementedException();
        }

        public bool Write(Cluster cluster)
        {
            _entry = cluster;
            throw new NotImplementedException();
        }

        public class State
        {
            internal readonly BaseModel Container;

            public State(BaseModel container)
            {
                Container = container;
            }
        }

    }
}