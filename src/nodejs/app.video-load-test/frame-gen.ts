// Synthetic video frame generator + MessagePack encoder.
// Matches the C# App.VideoLoadTest GenerateFrame + SerializeVideoFrame so the
// TS results can be compared to C# numbers directly. Frame payload sizes, GOP,
// and wire format must stay identical to preserve apples-to-apples timings.

import { randomBytes } from 'node:crypto';
import { toInt64, type VideoFrameDto } from './service-defs.js';

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

// Shared payload templates, filled once at module load with random bytes so
// the wire data matches what the C# harness sends (high-entropy → zero
// compression benefit, same observed encode cost). Reused across every
// frame to eliminate ~20 MB/s of throwaway Uint8Array allocation +
// crypto.randomBytes work that was visible in the 300-pull profile as
// FastBuffer / GC pressure. The first 2 bytes are overwritten per frame
// with the index so downstream content-inspection tooling can still
// recover frame ordering if needed.
const KEYFRAME_TEMPLATE: Uint8Array = new Uint8Array(randomBytes(FrameConfig.KeyFrameDataSize));
const DELTA_TEMPLATE: Uint8Array = new Uint8Array(randomBytes(FrameConfig.DeltaFrameDataSize));
const KEYFRAME_DESCRIPTION: Uint8Array = new Uint8Array([0x00, 0x00, 0x00, 0x01, 0x67]);

/** Build a synthetic VideoFrame whose .NET `VideoFrame` DTO bytes and timing
 *  anchors match the C# test.
 *
 *  The first two bytes of `Data` encode the frame index so a receiver can
 *  reconstruct the producer-side index without a side-channel. Note that the
 *  returned DTO borrows a reference to the shared payload template — the
 *  MessagePack encoder copies the bytes out into the wire payload on
 *  `encoder.encode(dto)`, so later mutation of `data[0]`/`data[1]` for the
 *  next frame does not affect already-encoded messages.
 */
export function generateFrame(index: number): VideoFrameDto {
    const isKeyFrame = index % FrameConfig.GopSize === 0;
    const data = isKeyFrame ? KEYFRAME_TEMPLATE : DELTA_TEMPLATE;
    data[0] = index & 0xff;
    data[1] = (index >> 8) & 0xff;

    const dto: VideoFrameDto = {
        Data: data,
        Offset: toInt64(FrameConfig.FrameDurationTicks * index),
        Duration: toInt64(FrameConfig.FrameDurationTicks),
        IsKeyFrame: isKeyFrame,
    };
    if (isKeyFrame) {
        dto.Width = FrameConfig.Width;
        dto.Height = FrameConfig.Height;
        dto.Description = KEYFRAME_DESCRIPTION;
        dto.Codec = FrameConfig.Codec;
    }
    return dto;
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
