export type Int64 = number | bigint;

export function toInt64(value: number): bigint {
    return BigInt(Math.trunc(value));
}

export function int64ToNumber(value: Int64): number {
    return typeof value === 'bigint' ? Number(value) : value;
}

export type Moment = Int64;

const MOMENT_TICKS_PER_SECOND = 10_000_000;

export function toMoment(ticks: number): bigint {
    return toInt64(ticks);
}

export function momentToNumber(ticks: Moment): number {
    return int64ToNumber(ticks);
}

export function secondsToMoment(seconds: number): bigint {
    return toMoment(seconds * MOMENT_TICKS_PER_SECOND);
}

export function momentToSeconds(ticks: Moment): number {
    return momentToNumber(ticks) / MOMENT_TICKS_PER_SECOND;
}
