namespace ActualChat.Audio.Ogg;

[StructLayout(LayoutKind.Sequential)]
public struct OpusTags
{
    public const ulong Signature = 0x4F_70_75_73_54_61_67_73; // OpusTags
    public const uint VendorStringLength = 16;
    public const string VendorString = "ActualChat Voice";
    public const uint UserCommentListLength = 0;

    public static byte Size => (byte)(sizeof(ulong) + sizeof(uint) + VendorStringLength) + sizeof(uint);
}
