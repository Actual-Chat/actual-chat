
export type DecoderCommandType = 'loadDecoder' | 'init' | 'pushData' | 'getDecodedData' | 'endOfStream' | 'stop';

export interface IDecoderCommand {
    command: DecoderCommandType;
}

export type DecoderCommand =
    LoadDecoderCommand |
    InitCommand |
    PushDataCommand |
    GetDecodedDataCommand |
    EndOfStreamCommand |
    StopCommand;

export class LoadDecoderCommand implements IDecoderCommand {
    public readonly command: DecoderCommandType = 'loadDecoder';
}

export class InitCommand implements IDecoderCommand {
    public readonly command: DecoderCommandType = 'init';
    public readonly header: ArrayBuffer;

    constructor(header: ArrayBuffer) {
        this.header = header;
    }
}

export class PushDataCommand implements IDecoderCommand {
    public readonly command: DecoderCommandType = 'pushData';
    public readonly buffer: ArrayBuffer;

    constructor(buffer: ArrayBuffer) {
        this.buffer = buffer;
    }
}

export class GetDecodedDataCommand implements IDecoderCommand {
    public readonly command: DecoderCommandType = 'getDecodedData';
}

export class EndOfStreamCommand implements IDecoderCommand {
    public readonly command: DecoderCommandType = 'endOfStream';
}

export class StopCommand implements IDecoderCommand {
    public readonly command: DecoderCommandType = 'stop';
}

export interface DecoderMessage {
    topic: 'samples';
    offset: number;
    length: number;
    buffer: ArrayBuffer;
}

export interface DecoderWorkerMessage {
    topic: 'readyToInit' | 'initCompleted';
}

export interface DecoderWorkletMessage {
    topic: 'buffer';
    buffer: ArrayBuffer;
}
