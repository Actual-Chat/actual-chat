using System.Net;
using System.Security.Cryptography;
using System.Text;
using ActualChat.Users.Module;

namespace ActualChat.Users;

// Implements TOTP code generation & verification per RFC 6238.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

public sealed class TotpCodes(UsersSettings settings)
{
    private static readonly Encoding Encoding = new UTF8Encoding(false, true);
    private UsersSettings Settings { get; } = settings;

    public int Generate(byte[] securityToken, string? modifier = null)
    {
        // Allow a variance of no greater than TimeStepCount * TimeStep in either direction
        var currentTimeStep = GetCurrentTimeStepNumber();

        var modifierBytes = modifier is not null ? Encoding.GetBytes(modifier) : null;
        return Compute(securityToken, currentTimeStep, modifierBytes);
    }

    public bool Validate(byte[] securityToken, int code, string? modifier = null)
    {
        // Allow a variance of no greater than TimeStepCount * TimeStep in either direction
        var currentTimeStep = GetCurrentTimeStepNumber();

        var modifierBytes = modifier is not null ? Encoding.GetBytes(modifier) : null;
        for (var i = -Settings.TotpTimestepCount; i <= Settings.TotpTimestepCount; i++) {
            var computedTotp = Compute(securityToken, (ulong)((long)currentTimeStep + i), modifierBytes);
            if (computedTotp == code)
                return true;
        }

        // No match
        return false;
    }

    // Private methods

    private static int Compute(
        byte[] key,
        ulong timestepNumber,
        byte[]? modifierBytes)
    {
        // # of 0's = length of pin
        var mod = (int)Math.Pow(10, Constants.Auth.Phone.TotpLength);

        // See https://tools.ietf.org/html/rfc4226
        // We can add an optional modifier
        Span<byte> timestepAsBytes = stackalloc byte[sizeof(long)];
        var res = BitConverter.TryWriteBytes(timestepAsBytes, IPAddress.HostToNetworkOrder((long)timestepNumber));
        Debug.Assert(res);

        Span<byte> modifierCombinedBytes = timestepAsBytes;
        if (modifierBytes is not null)
        {
            modifierCombinedBytes = ApplyModifier(timestepAsBytes, modifierBytes);
        }
        Span<byte> hash = stackalloc byte[HMACSHA1.HashSizeInBytes];
#pragma warning disable CA5350
        res = HMACSHA1.TryHashData(key, modifierCombinedBytes, hash, out var written);
#pragma warning restore CA5350
        Debug.Assert(res);
        Debug.Assert(written == hash.Length);

        // Generate DT string
        var offset = hash[^1] & 0xf;
        Debug.Assert(offset + 4 < hash.Length);
        var binaryCode = ((hash[offset] & 0x7f) << 24)
            | ((hash[offset + 1] & 0xff) << 16)
            | ((hash[offset + 2] & 0xff) << 8)
            | (hash[offset + 3] & 0xff);

        return binaryCode % mod;
    }

    private static byte[] ApplyModifier(Span<byte> input, byte[] modifierBytes)
    {
        var combined = new byte[checked(input.Length + modifierBytes.Length)];
        input.CopyTo(combined);
        Buffer.BlockCopy(modifierBytes, 0, combined, input.Length, modifierBytes.Length);
        return combined;
    }

    // More info: https://tools.ietf.org/html/rfc6238#section-4
    private ulong GetCurrentTimeStepNumber()
    {
        var delta = DateTimeOffset.UtcNow - DateTimeOffset.UnixEpoch;
        return (ulong)(delta.Ticks / Settings.TotpTimestep.Ticks);
    }
}
