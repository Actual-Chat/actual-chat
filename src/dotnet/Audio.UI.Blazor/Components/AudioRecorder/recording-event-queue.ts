import Denque from "denque";

const LogScope = 'RecordingEventQueue';

export enum RecordingEventType {
    Data = 1,
    Pause,
    Resume,
    Voice,
    Timestamp
}

export interface IRecordingEvent {
    type: RecordingEventType;
    serialize(buffer: Uint8Array, offset: number): number;
}

export class DataRecordingEvent implements IRecordingEvent {
    private readonly data: Uint8Array;

    public readonly type: RecordingEventType;

    constructor(data: Uint8Array) {
        this.data = data;
        this.type = RecordingEventType.Data;
    }

    serialize(buffer: Uint8Array, offset: number): number {
        const bufferLength = buffer.length;
        const length = 3 + this.data.length;
        const remaining = bufferLength - offset - length;
        if (remaining < 0)
            return remaining;

        const dataView = new DataView(buffer.buffer, offset);
        dataView.setUint8(0, this.type);
        dataView.setUint16(1, this.data.length, true);
        buffer.set(this.data, offset + 3);
        return length;
    }
}

export class PauseRecordingEvent implements IRecordingEvent {
    public readonly type: RecordingEventType;

    constructor() {
        this.type = RecordingEventType.Pause;
    }

    serialize(buffer: Uint8Array, offset: number): number {
        const bufferLength = buffer.length;
        const length = 4;
        const remaining = bufferLength - offset - length;
        if (remaining < 0)
            return remaining;

        const dataView = new DataView(buffer.buffer, offset);
        dataView.setUint8(0, this.type);
        dataView.setUint16(1, 1, true);
        dataView.setUint8(3, 0);
        return length;
    }
}

export class ResumeRecordingEvent implements IRecordingEvent {
    public readonly type: RecordingEventType;

    constructor() {
        this.type = RecordingEventType.Resume;
    }

    serialize(buffer: Uint8Array, offset: number): number {
        const bufferLength = buffer.length;
        const length = 4;
        const remaining = bufferLength - offset - length;
        if (remaining < 0)
            return remaining;

        const dataView = new DataView(buffer.buffer, offset);
        dataView.setUint8(0, this.type);
        dataView.setUint16(1, 1, true);
        dataView.setUint8(3, 1);
        return length;
    }
}

export class TimestampRecordingEvent implements IRecordingEvent {
    private readonly _ticks: bigint;

    public readonly type: RecordingEventType;

    constructor(now: number, timeSlice: number) {
        this.type = RecordingEventType.Pause;
        this._ticks = BigInt((Date.now() - timeSlice) * 1e4 + 621355968e9);
    }

    serialize(buffer: Uint8Array, offset: number): number {
        const bufferLength = buffer.length;
        const length = 11;
        const remaining = bufferLength - offset - length;
        if (remaining < 0)
            return remaining;

        const dataView = new DataView(buffer.buffer, offset);
        dataView.setUint8(0, this.type);
        dataView.setUint16(1, 8, true);
        dataView.setBigUint64(3, this._ticks);
        return length;
    }
}

export type RecordingEvent =
    DataRecordingEvent |
    PauseRecordingEvent |
    ResumeRecordingEvent |
    TimestampRecordingEvent;

export interface IRecordingEventQueue {
    append(command: RecordingEvent): void;
    flushAsync(): Promise<void>;
}

export interface IRecordingEventQueueOptions {
    chunkSize: number;
    minChunkSize: number;
    maxFillBufferTimeMs: number;
    sendAsync: (data: Uint8Array) => Promise<void>;
    debugMode: boolean;
}

export class RecordingEventQueue implements IRecordingEventQueue {
    private _state: 'running' | 'paused';
    private _buffer: Uint8Array;
    private _bufferOffset: number;
    private readonly _lastBlocks: Denque<Uint8Array>;
    private readonly _bufferQueue: Denque<Uint8Array>;
    private readonly _options: IRecordingEventQueueOptions;
    private _sendBufferTimeout?: ReturnType<typeof setTimeout>;

    constructor(options: IRecordingEventQueueOptions) {
        this._options = options;
        this._buffer = new Uint8Array(this._options.chunkSize);
        this._bufferOffset = 0;
        this._sendBufferTimeout = null;
        this._state = 'running';
        this._lastBlocks = new Denque<Uint8Array>();
        this._bufferQueue = new Denque<Uint8Array>();
        this._bufferQueue.push(new Uint8Array(1024));
        this._bufferQueue.push(new Uint8Array(1024));
        this._bufferQueue.push(new Uint8Array(1024));
        this._bufferQueue.push(new Uint8Array(1024));
        this._bufferQueue.push(new Uint8Array(1024));
        this._bufferQueue.push(new Uint8Array(1024));
        this._bufferQueue.push(new Uint8Array(1024));
        this._bufferQueue.push(new Uint8Array(1024));
    }

    append(command: IRecordingEvent): void {
        if (this._options.debugMode) {
            this.log(`append: ${command.type}`);
        }

        let sendImmediately = false;
        switch (command.type) {
            case RecordingEventType.Pause:
                this._state = "paused";
                sendImmediately = true;
                break;
            case RecordingEventType.Resume:
                if (this._state === "running")
                    return;

                sendImmediately = true;
                this._state = "running";
                break;
            case RecordingEventType.Data:
            case RecordingEventType.Voice:
            case RecordingEventType.Timestamp:
                break;
        }

        if (this._bufferQueue.length == 0) {
            this._bufferQueue.push(new Uint8Array(1024));
        }
        let commandLength = command.serialize(this._bufferQueue.peekFront(), this._bufferOffset);
        if (sendImmediately || commandLength < 0) {
            const _ = this.sendTopmostBuffer();
        }
        if (commandLength < 0) {
            commandLength = command.serialize(this._bufferQueue.peekFront(), this._bufferOffset);
            if (sendImmediately) {
                const _ = this.sendTopmostBuffer();
            }
        }
        this._bufferOffset += commandLength;

        if (this._bufferOffset >= this._options.minChunkSize) {
            const _ = this.sendTopmostBuffer();
        } else {
            if (this._options.debugMode) {
                this.log(`enqueue: ${this._bufferOffset} byte(s) were left in buffer`);
            }
        }
        this.ensureSentByTimeout();
    }

    public async flushAsync(): Promise<void> {
        if (this._options.debugMode) {
            this.log(`flushAsync is called`);
        }
        await this.sendTopmostBuffer();
    }


    private log(message: string) {
        console.debug(`${LogScope}: ${message}`);
    }

    private logError(message: string) {
        console.error(`${LogScope}: ${message}`);
    }

    private ensureSentByTimeout() {
        if (this._sendBufferTimeout === null) {
            this._sendBufferTimeout = setTimeout(() => {
                if (this._options.debugMode) {
                    this.log(`Send timeout is fired, sending buffer with ${this._bufferOffset} data bytes.`);
                }
                const _ = this.sendTopmostBuffer();
            }, this._options.maxFillBufferTimeMs);
        }
    }

    private clearSendTimeout() {
        if (this._sendBufferTimeout !== null) {
            clearTimeout(this._sendBufferTimeout);
            this._sendBufferTimeout = null;
        }
    }

    private async sendTopmostBuffer(): Promise<void> {
        const currentDataLength = this._bufferOffset;
        if (currentDataLength == 0)
            return;

        this._bufferOffset = 0;
        const buffer = await this.sendBufferAsync(this._bufferQueue.shift(), currentDataLength);
        this._bufferQueue.push(buffer);
    }

    private async sendBufferAsync(buffer: Uint8Array, length: number): Promise<Uint8Array> {
        this.clearSendTimeout();

        if (this._state === "paused")
            return buffer;

        if (length === 0) {
            if (this._options.debugMode) {
                this.log(`Buffer is empty.`);
            }
            return buffer;
        }
        const packetBytes = buffer.subarray(0, length);
        try {
            await this._options.sendAsync(packetBytes);
            if (this._options.debugMode) {
                this.log(`Sent ${length} data bytes`);
            }
        }
        catch (error) {
            if (this._options.debugMode) {
                this.logError(`Couldn't send ${length} data bytes, error: ${error}`);
            }
        }
        return buffer;
    }

}

export default RecordingEventQueue;
