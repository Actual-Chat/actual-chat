using System;
using System.Runtime.InteropServices;

namespace ActualChat.Audio.Ebml.Backup
{
    [StructLayout(LayoutKind.Explicit)]
    internal struct Union
    {
        [FieldOffset(0)]
        public UInt32 uival;
        [FieldOffset(0)]
        public float fval;

        [FieldOffset(0)]
        public UInt64 ulval;
        [FieldOffset(0)]
        public double dval;
    }
}