import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { SenderSession } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/sender/session';
import { MonotonicClock } from 'clocks';

// Minimal `WritableStream<VideoFrame>`-shaped fake for preview testing.
// Tracks whether `getWriter()` was called and whether the issued writer's
// lock was released.
class FakeWritableStream {
    public writerCount = 0;
    public locked = false;
    public lockReleased = false;
    public throwOnGetWriter = false;
    getWriter(): WritableStreamDefaultWriter<VideoFrame> {
        if (this.throwOnGetWriter) throw new Error('locked');
        if (this.locked) throw new Error('already locked');
        this.locked = true;
        this.writerCount++;
        const releaseLock = (): void => {
            this.lockReleased = true;
            this.locked = false;
        };
        return { releaseLock } as unknown as WritableStreamDefaultWriter<VideoFrame>;
    }
}

class MockVideoEncoder {
    state = 'configured';
    constructor(public init: object) {}
    encode(): void { /* no-op */ }
    close(): void { /* no-op */ }
}

interface GlobalWithVideoEncoder {
    VideoEncoder?: typeof MockVideoEncoder;
}

beforeEach(() => {
    (globalThis as unknown as GlobalWithVideoEncoder).VideoEncoder = MockVideoEncoder;
});

afterEach(() => {
    delete (globalThis as unknown as GlobalWithVideoEncoder).VideoEncoder;
});

describe('SenderSession', () => {
    it('constructs with default capture clock, no preview writer when generator omitted', () => {
        const session = new SenderSession();
        expect(session.captureClock).toBeInstanceOf(MonotonicClock);
        expect(session.getPreviewWriter()).toBeNull();
        session.dispose();
    });

    it('locks a writer when previewGenerator is supplied', () => {
        const stream = new FakeWritableStream();
        const session = new SenderSession({
            previewGenerator: { writable: stream as unknown as WritableStream<VideoFrame> },
        });
        expect(session.getPreviewWriter()).not.toBeNull();
        expect(stream.writerCount).toBe(1);
        expect(stream.locked).toBe(true);
        session.dispose();
        // dispose() releases the writer's lock.
        expect(stream.lockReleased).toBe(true);
    });

    it('previewWriter is null when getWriter throws (e.g. already locked)', () => {
        const stream = new FakeWritableStream();
        stream.throwOnGetWriter = true;
        const session = new SenderSession({
            previewGenerator: { writable: stream as unknown as WritableStream<VideoFrame> },
        });
        expect(session.getPreviewWriter()).toBeNull();
        session.dispose();
    });

    it('can swap preview generators between runs', () => {
        const first = new FakeWritableStream();
        const second = new FakeWritableStream();
        const session = new SenderSession({
            previewGenerator: { writable: first as unknown as WritableStream<VideoFrame> },
        });

        session.setPreviewGenerator({ writable: second as unknown as WritableStream<VideoFrame> });

        expect(first.lockReleased).toBe(true);
        expect(first.locked).toBe(false);
        expect(second.writerCount).toBe(1);
        expect(second.locked).toBe(true);
        expect(session.getPreviewWriter()).not.toBeNull();
        session.dispose();
    });

    it('dispose is idempotent', () => {
        const session = new SenderSession();
        expect(session.isDisposed).toBe(false);
        session.dispose();
        expect(session.isDisposed).toBe(true);
        // Second call is a no-op (no throw).
        session.dispose();
        expect(session.isDisposed).toBe(true);
    });
});
