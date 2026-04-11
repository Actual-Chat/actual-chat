// Synthetic video frame generator + MessagePack encoder.
// Matches the C# App.VideoLoadTest GenerateFrame + SerializeVideoFrame so the
// TS results can be compared to C# numbers directly. Frame payload sizes, GOP,
// and wire format must stay identical to preserve apples-to-apples timings.

import { encode as msgpackEncode } from '@msgpack/msgpack';
import { randomBytes } from 'node:crypto';
import type { VideoFrameDto } from './service-defs.js';

export const FrameConfig = {
    GopSize: 30,
    KeyFrameDataSize: 40_000,
    DeltaFrameDataSize: 10_000,
    Width: 1280,
    Height: 720,
    Codec: 'avc1',
    /** .NET TimeSpan.TicksPerSecond = 10^7 → 30fps = 333_333 ticks/frame. */
    FrameDurationTicks: Math.floor(10_000_000 / 30),
} as const;

/** Build a synthetic VideoFrame whose .NET `VideoFrame` DTO bytes and timing
 *  anchors match the C# test. Returns both the TS DTO (for RPC push) and the
 *  MessagePack bytes (for SignalR push).
 *
 *  The first two bytes of `Data` encode the frame index, so a receiver can
 *  reconstruct the producer-side index without needing a side-channel — useful
 *  if you wanted to cross-check latency pairing by content.
 */
export function generateFrame(index: number): VideoFrameDto {
    const isKeyFrame = index % FrameConfig.GopSize === 0;
    const dataSize = isKeyFrame ? FrameConfig.KeyFrameDataSize : FrameConfig.DeltaFrameDataSize;
    const data = new Uint8Array(randomBytes(dataSize));
    data[0] = index & 0xff;
    data[1] = (index >> 8) & 0xff;

    const dto: VideoFrameDto = {
        Data: data,
        Offset: FrameConfig.FrameDurationTicks * index,
        Duration: FrameConfig.FrameDurationTicks,
        IsKeyFrame: isKeyFrame,
    };
    if (isKeyFrame) {
        dto.Width = FrameConfig.Width;
        dto.Height = FrameConfig.Height;
        dto.Description = new Uint8Array([0x00, 0x00, 0x00, 0x01, 0x67]);
        dto.Codec = FrameConfig.Codec;
    }
    return dto;
}

/**
 * Serialize a VideoFrameDto to MessagePack bytes for the SignalR `PushVideo`
 * byte[] path. Key order and presence must match the server's
 * `[MessagePackObject(true)]` VideoFrame so deserialization populates every
 * property. `@msgpack/msgpack.encode` produces a map with the keys in the
 * insertion order of the object literal, which is what we rely on.
 */
export function encodeFrameForSignalR(dto: VideoFrameDto): Uint8Array {
    const obj: Record<string, unknown> = {
        Offset: dto.Offset,
        Duration: dto.Duration,
        Data: dto.Data,
    };
    if (dto.IsKeyFrame) {
        obj.IsKeyFrame = true;
        if (dto.Width !== undefined) obj.Width = dto.Width;
        if (dto.Height !== undefined) obj.Height = dto.Height;
    }
    if (dto.Description) obj.Description = dto.Description;
    if (dto.Codec) obj.Codec = dto.Codec;
    return msgpackEncode(obj);
}

/**
 * Pace a synchronous generator loop at the configured frame rate. Returns a
 * promise that resolves after `delayMs`, aligned so the N-th frame fires at
 * T0 + N * frameDurationMs. Falls through immediately if the target time has
 * already passed.
 */
export async function paceFrame(
    startWallClock: number,
    index: number,
): Promise<void> {
    const targetMs = (FrameConfig.FrameDurationTicks / 10_000) * index;
    const elapsed = Date.now() - startWallClock;
    const remaining = targetMs - elapsed;
    if (remaining > 0) {
        await new Promise<void>((r) => setTimeout(r, remaining));
    }
}
