namespace ActualChat.Audio.Ogg;

[StructLayout(LayoutKind.Sequential)]
public struct OpusTags
{
    public const ulong Signature = 0x73_67_61_54_73_75_70_4F; // OpusTags
    public const uint VendorStringLength = 16;
    public const string VendorString = "ActualChat Voice";

    public static byte Size => (byte)(sizeof(ulong) + sizeof(uint) + VendorStringLength);
}
