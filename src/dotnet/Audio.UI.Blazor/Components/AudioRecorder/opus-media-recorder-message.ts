export type EncoderCommandType = 'loadEncoder' | 'init' | 'pushInputData' | 'getEncodedData' | 'done';

export interface IEncoderCommand {
    command: EncoderCommandType;
}

const mimeType: string = 'audio/webm';
export class LoadEncoderCommand implements IEncoderCommand {
    public readonly command: EncoderCommandType = 'loadEncoder';
    public readonly mimeType = mimeType;
    public readonly wasmPath: string;

    constructor(wasmPath: string) {
        this.wasmPath = wasmPath;
    }
}

export class InitCommand implements IEncoderCommand {
    public readonly command: EncoderCommandType = 'init';
    public sampleRate: number = 48000;
    public channelCount: number = 1;
    public bitsPerSecond: number = 32000;

    constructor(sampleRate: number, channelCount: number, bitsPerSecond: number) {
        this.sampleRate = sampleRate;
        this.channelCount = channelCount;
        this.bitsPerSecond = bitsPerSecond;
    }
}

export class PushInputDataCommand implements IEncoderCommand {
    public readonly command: EncoderCommandType = 'pushInputData';
    public channelBuffers: Float32Array[];

    constructor(channelBuffers: Float32Array[]) {
        this.channelBuffers = channelBuffers;
    }
}

export class GetEncodedDataCommand implements IEncoderCommand {
    public readonly command: EncoderCommandType = 'getEncodedData';
}

export class DoneCommand implements IEncoderCommand {
    public readonly command: EncoderCommandType = 'done';
}

export type EncoderCommand =
    LoadEncoderCommand |
    InitCommand |
    PushInputDataCommand |
    GetEncodedDataCommand |
    DoneCommand;

export interface EncoderMessage {
    command: 'readyToInit' | 'lastEncodedData' | 'encodedData';
    buffers: ArrayBuffer[];
}
