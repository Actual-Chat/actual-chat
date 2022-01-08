
export type DecoderCommandType = 'loadDecoder' | 'init' | 'pushData' | 'endOfStream' | 'stop';

export interface IDecoderCommand {
    command: DecoderCommandType;
    playerId: string;
}

export type DecoderCommand =
    LoadDecoderCommand |
    InitCommand |
    PushDataCommand |
    EndOfStreamCommand |
    StopCommand;

export class LoadDecoderCommand implements IDecoderCommand {
    public readonly command: DecoderCommandType = 'loadDecoder';
    public readonly playerId: string = 'NA';

    constructor() {
    }
}

export class InitCommand implements IDecoderCommand {
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

export class PushDataCommand implements IDecoderCommand {
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

export class EndOfStreamCommand implements IDecoderCommand {
    public readonly command: DecoderCommandType = 'endOfStream';
    public readonly playerId: string;

    constructor(playerId: string) {
        this.playerId = playerId;
    }
}

export class StopCommand implements IDecoderCommand {
    public readonly command: DecoderCommandType = 'stop';
    public readonly playerId: string;

    constructor(playerId: string) {
        this.playerId = playerId;
    }
}

export interface DecoderMessage {
    topic: 'samples';
    offset: number;
    length: number;
    buffer: ArrayBuffer;
}

export interface DecoderWorkerMessage {
    topic: 'initCompleted';
    playerId: string;
}

export interface DecoderWorkletMessage {
    topic: 'buffer';
    buffer: ArrayBuffer;
}
