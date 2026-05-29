// Per-author audio presentation latency (ms), published by the audio player and
// read by the video player to align skip-to-audio (A/V-sync "audio-master under
// video skip"). Main-thread only — both players run there, so the value crosses
// from audio to video without a C# round-trip. Values are already EMA-smoothed
// by the publisher.

interface AudioLatencyEntry {
    latencyMs: number;
    atMs: number;
}

const entries = new Map<string, AudioLatencyEntry>();

export function publishAudioLatency(authorId: string, latencyMs: number): void {
    if (authorId.length === 0)
        return;
    entries.set(authorId, { latencyMs, atMs: Date.now() });
}

// Latest audio latency for the author, or null when absent or older than
// maxAgeMs (stale ⇒ the video pipeline falls back to skip-to-live).
export function getAudioLatency(authorId: string, maxAgeMs = 1500): number | null {
    const e = entries.get(authorId);
    if (e === undefined)
        return null;
    if (Date.now() - e.atMs > maxAgeMs)
        return null;
    return e.latencyMs;
}

export function clearAudioLatency(authorId: string): void {
    entries.delete(authorId);
}
