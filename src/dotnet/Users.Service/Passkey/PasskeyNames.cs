namespace ActualChat.Users.Passkeys;

/// <summary>
/// Default passkey names by authenticator AAGUID (passkeydeveloper/passkey-authenticator-aaguids).
/// </summary>
public static class PasskeyNames
{
    private static readonly Dictionary<Guid, string> ByAaguid = new() {
        [new("fbfc3007-154e-4ecc-8c0b-6e020557d7bd")] = "iCloud Keychain",
        [new("ea9b8d66-4d01-1d21-3ce4-b6b48cb575d4")] = "Google Password Manager",
        [new("08987058-cadc-4b81-b6e1-30de50dcbe96")] = "Windows Hello",
        [new("9ddd1817-af5a-4672-a2b9-3e3dd95000a9")] = "Windows Hello",
        [new("6028b017-b1d4-4c02-b4b3-afcdafc96bb2")] = "Windows Hello",
        [new("adce0002-35bc-c60a-648b-0b25f1f05503")] = "Chrome on Mac",
        [new("bada5566-a7aa-401f-bd96-45619a55120d")] = "1Password",
        [new("d548826e-79b4-db40-a3d8-11116f7e8349")] = "Bitwarden",
        [new("531126d6-e717-415c-9320-3d9aa6981239")] = "Dashlane",
        [new("0ea242b4-43c4-4a1b-8b17-dd6d0b6baec6")] = "Keeper",
        [new("b84e4048-15dc-4dd0-8640-f4f60813c8af")] = "NordPass",
        [new("f3809540-7f14-49c1-a8b3-8f813b225541")] = "Enpass",
        [new("50726f74-6f6e-5061-7373-50726f746f6e")] = "Proton Pass",
        [new("53414d53-554e-4700-0000-000000000000")] = "Samsung Pass",
    };

    public static string GetDefault(Guid aaguid, string transports)
    {
        if (ByAaguid.TryGetValue(aaguid, out var name))
            return name;

        var isRoaming = transports.Contains("usb") || transports.Contains("nfc") || transports.Contains("ble");
        return isRoaming ? "Security key" : "Passkey";
    }
}
