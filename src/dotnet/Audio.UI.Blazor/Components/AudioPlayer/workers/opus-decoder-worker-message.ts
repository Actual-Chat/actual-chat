
export type DecoderCommandType = 'loadDecoder' | 'init' | 'pushData' | 'endOfStream' | 'stop';

interface DecoderCommandContract {
    command: DecoderCommandType;
    playerId: string;
}

export type DecoderCommand =
    LoadDecoderCommand |
    InitCommand |
    PushDataCommand |
    EndOfStreamCommand |
    StopCommand;

export class LoadDecoderCommand implements DecoderCommandContract {
    public readonly command: DecoderCommandType = 'loadDecoder';
    public readonly playerId: string = 'NA';

    constructor() {
    }
}

export class InitCommand implements DecoderCommandContract {
    public readonly command: DecoderCommandType = 'init';
    public readonly playerId: string;
    public readonly buffer: ArrayBuffer;
    public readonly offset: number;
    public readonly length: number;

    constructor(playerId: string, buffer: ArrayBuffer, offset: number, length: number) {
        this.playerId = playerId;
        this.buffer = buffer;
        this.offset = offset;
        this.length = length;
    }
}

export class PushDataCommand implements DecoderCommandContract {
    public readonly command: DecoderCommandType = 'pushData';
    public readonly playerId: string;
    public readonly buffer: ArrayBuffer;
    public readonly offset: number;
    public readonly length: number;

    constructor(playerId: string, buffer: ArrayBuffer, offset: number, length: number) {
        this.playerId = playerId;
        this.buffer = buffer;
        this.offset = offset;
        this.length = length;
    }
}

export class EndOfStreamCommand implements DecoderCommandContract {
    public readonly command: DecoderCommandType = 'endOfStream';
    public readonly playerId: string;

    constructor(playerId: string) {
        this.playerId = playerId;
    }
}

export class StopCommand implements DecoderCommandContract {
    public readonly command: DecoderCommandType = 'stop';
    public readonly playerId: string;

    constructor(playerId: string) {
        this.playerId = playerId;
    }
}

//** Decoded samples, will be consumed at the decoder worklet */
export interface DecoderMessage {
    topic: 'samples';
    offset: number;
    length: number;
    buffer: ArrayBuffer;
}

//** Init callback message, handled at the audio player main thread */
export interface DecoderWorkerMessage {
    topic: 'initCompleted';
    playerId: string;
}

//** Processed buffer to be returned back to the decoder worker */
export interface DecoderWorkletMessage {
    topic: 'buffer';
    buffer: ArrayBuffer;
}
