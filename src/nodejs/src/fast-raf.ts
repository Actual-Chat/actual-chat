// Frame scheduler with two phases: every read registered for a frame runs before every write
// registered for it, across all frequencies that fall due together. That ordering is the whole
// point - a write between two reads makes the second one force a synchronous layout, and on an
// iPhone 13 Pro one such inversion cost 1461ms of forced layout per 30s call.
//
// `hz` limits a callback to a fixed step instead of the next frame: 10Hz is AnimationSync's
// 100ms grid, so a stream-driven write scheduled at 10 or 20Hz lands in a frame the synced
// animations were changing anyway rather than adding one of its own - and since the rendering
// cost is per changed frame, not per changed element, that is the whole difference.
//
// Best-effort by design: the test is that the instant has passed, not that this frame sits on
// it, so a late or dropped frame fires on the next one rather than waiting out another period.
// Concurrent callers at one frequency share a step and so land together.

export type Callback = (time: number) => void;

export interface FastRafSchedule {
    key?: string;
    // Frequency in Hz - 5 is a 200ms step, 10 the animation grid. Absent means the next frame.
    hz?: number;
}

export interface FastRafOptions extends FastRafSchedule {
    read?: Callback;
    write?: Callback;
}

interface Bucket {
    dueAt: number;
    reads: Callback[];
    writes: Callback[];
    keys: Set<string>;
}

// A pending requestAnimationFrame makes the browser run the whole rendering lifecycle for that
// frame, so polling rAF up to an instant puts a style recalc in every frame along the way -
// measured in Chrome at 99.6% of frames against a paint in 10.6%. A stepped bucket therefore
// sleeps on a timer and only starts asking for frames this long before its instant, which also
// makes it independent of timer resolution: iOS coalesces timers and Windows can drop to ~15ms,
// and waking early lands the callback within a frame of the instant either way.
const leadInMs = 20;

const buckets = new Map<number, Bucket>();
const readRafPromises = new Map<number, Promise<boolean>>();
const writeRafPromises = new Map<number, Promise<boolean>>();
let timeoutId: ReturnType<typeof setTimeout> | undefined;
let isFrameRequested = false;
let wakeUpAt = Infinity;

export function fastRaf(options: FastRafOptions): boolean;
export function fastRaf(read: Callback, key?: string): boolean;
export function fastRaf(arg: Callback | FastRafOptions, key?: string): boolean {
    const options: FastRafOptions = typeof arg === 'function' ? { read: arg } : arg;
    const bucket = bucketOf(stepMsOf(options.hz));
    const bucketKey = key ?? options.key;
    if (bucketKey) {
        if (bucket.keys.has(bucketKey))
            return false;

        bucket.keys.add(bucketKey);
    }
    if (options.read)
        bucket.reads.push(options.read);
    if (options.write)
        bucket.writes.push(options.write);

    return true;
}

export function fastReadRafAsync(key?: string): Promise<boolean>;
export function fastReadRafAsync(schedule: FastRafSchedule): Promise<boolean>;
export function fastReadRafAsync(arg?: string | FastRafSchedule): Promise<boolean> {
    return phaseRaf(readRafPromises, arg, false);
}

export function fastWriteRafAsync(key?: string): Promise<boolean>;
export function fastWriteRafAsync(schedule: FastRafSchedule): Promise<boolean>;
export function fastWriteRafAsync(arg?: string | FastRafSchedule): Promise<boolean> {
    return phaseRaf(writeRafPromises, arg, true);
}

export const fastRaf5 = fastRafAt(5);
export const fastRaf10 = fastRafAt(10);
export const fastRaf20 = fastRafAt(20);
export const fastRaf30 = fastRafAt(30);

export const fastReadRaf5Async = phaseRafAt(readRafPromises, 5, false);
export const fastReadRaf10Async = phaseRafAt(readRafPromises, 10, false);
export const fastReadRaf20Async = phaseRafAt(readRafPromises, 20, false);
export const fastReadRaf30Async = phaseRafAt(readRafPromises, 30, false);

export const fastWriteRaf5Async = phaseRafAt(writeRafPromises, 5, true);
export const fastWriteRaf10Async = phaseRafAt(writeRafPromises, 10, true);
export const fastWriteRaf20Async = phaseRafAt(writeRafPromises, 20, true);
export const fastWriteRaf30Async = phaseRafAt(writeRafPromises, 30, true);

// Private methods

function fastRafAt(hz: number) {
    function scheduleAt(options: FastRafOptions): boolean;
    function scheduleAt(read: Callback, key?: string): boolean;
    function scheduleAt(arg: Callback | FastRafOptions, key?: string): boolean {
        return typeof arg === 'function'
            ? fastRaf({ hz, read: arg, key })
            : fastRaf({ ...arg, hz });
    }
    return scheduleAt;
}

function phaseRafAt(promises: Map<number, Promise<boolean>>, hz: number, isWrite: boolean) {
    return (key?: string): Promise<boolean> => phaseRaf(promises, { hz, key }, isWrite);
}

function phaseRaf(
    promises: Map<number, Promise<boolean>>,
    arg: string | FastRafSchedule | undefined,
    isWrite: boolean,
): Promise<boolean> {
    const schedule: FastRafSchedule = typeof arg === 'string' ? { key: arg } : arg ?? {};
    const stepMs = stepMsOf(schedule.hz);
    const pending = promises.get(stepMs);
    if (pending !== undefined)
        return pending;

    const promise = new Promise<boolean>(resolve => {
        const callback = () => resolve(true);
        const options: FastRafOptions = { hz: schedule.hz, key: schedule.key };
        if (isWrite)
            options.write = callback;
        else
            options.read = callback;
        if (!fastRaf(options))
            resolve(false);
    });
    promises.set(stepMs, promise);
    void promise.then(() => promises.delete(stepMs));
    return promise;
}

function stepMsOf(hz: number | undefined): number {
    return hz !== undefined && hz > 0 ? 1000 / hz : 0;
}

function bucketOf(stepMs: number): Bucket {
    let bucket = buckets.get(stepMs);
    if (bucket !== undefined)
        return bucket;

    const now = performance.now();
    bucket = {
        dueAt: stepMs > 0 ? (Math.floor(now / stepMs) + 1) * stepMs : now,
        reads: [],
        writes: [],
        keys: new Set<string>(),
    };
    buckets.set(stepMs, bucket);
    arm();
    return bucket;
}

function arm(): void {
    let dueAt = Infinity;
    let stepMs = 0;
    for (const [bucketStepMs, bucket] of buckets)
        if (bucket.dueAt < dueAt) {
            dueAt = bucket.dueAt;
            stepMs = bucketStepMs;
        }
    // A pending frame is already the soonest possible wake-up.
    if (isFrameRequested || dueAt === Infinity)
        return;
    if (timeoutId !== undefined && dueAt >= wakeUpAt)
        return;

    if (timeoutId !== undefined)
        clearTimeout(timeoutId);
    wakeUpAt = dueAt;
    const leadIn = stepMs > 0 ? Math.min(leadInMs, stepMs / 4) : 0;
    const delay = dueAt - performance.now() - leadIn;
    if (delay <= 0) {
        timeoutId = undefined;
        requestFrame();
        return;
    }

    timeoutId = setTimeout(() => {
        timeoutId = undefined;
        requestFrame();
    }, delay);
}

function requestFrame(): void {
    if (isFrameRequested)
        return;

    isFrameRequested = true;
    requestAnimationFrame(time => void onFrame(time));
}

async function onFrame(time: number): Promise<void> {
    isFrameRequested = false;
    wakeUpAt = Infinity;
    const due = new Array<Bucket>();
    for (const [stepMs, bucket] of buckets)
        if (bucket.dueAt <= time) {
            due.push(bucket);
            buckets.delete(stepMs);
        }
    if (due.length === 0) {
        // Woke inside the lead-in, so the instant is a frame or two away.
        requestFrame();
        return;
    }

    arm();
    for (const bucket of due)
        for (const read of bucket.reads)
            read(time);

    // Lets the continuation of an awaited fastReadRafAsync do its reads before any write lands -
    // resolving a promise only queues a microtask, so without this the continuation would run
    // after both phases, which is the inversion this scheduler exists to prevent.
    await Promise.resolve();
    for (const bucket of due)
        for (const write of bucket.writes)
            write(time);
}
