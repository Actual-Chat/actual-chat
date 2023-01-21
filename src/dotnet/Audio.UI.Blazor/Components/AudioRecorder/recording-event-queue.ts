import Denque from "denque";
import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'RecordingEventQueue';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

export enum RecordingEventType {
    Data = 1,
    Pause,
    Resume,
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

class CommandRecordingEvent implements IRecordingEvent {
    private readonly ticks: bigint;
    private readonly offset: number;

    public readonly type: RecordingEventType;

    constructor(type: RecordingEventType, now: number, offset: number) {
        this.type = type;
        this.ticks = BigInt(now * 1e4);
        this.offset = offset;
    }

    serialize(buffer: Uint8Array, offset: number): number {
        const bufferLength = buffer.length;
        const length = 15;
        const remaining = bufferLength - offset - length;
        if (remaining < 0)
            return remaining;

        const dataView = new DataView(buffer.buffer, offset);
        dataView.setUint8(0, this.type);
        dataView.setUint16(1, 12, true);
        dataView.setBigUint64(3, this.ticks, true);
        dataView.setFloat32(11, this.offset, true);
        return length;
    }
}

export class PauseRecordingEvent extends CommandRecordingEvent {
    constructor(now: number, offset: number) {
        super(RecordingEventType.Pause, now, offset);
    }
}

export class ResumeRecordingEvent extends CommandRecordingEvent {
    constructor(now: number, offset: number) {
        super(RecordingEventType.Resume, now, offset);
    }
}

export type RecordingEvent =
    DataRecordingEvent |
    PauseRecordingEvent |
    ResumeRecordingEvent;

export interface IRecordingEventQueue {
    append(event: RecordingEvent): void;
    flushAsync(): Promise<void>;
}

export interface IRecordingEventQueueOptions {
    chunkSize: number;
    minChunkSize: number;
    maxFillBufferTimeMs: number;
    sendAsync: (data: Uint8Array) => Promise<void>;
}

type QueueState = 'inactive' | 'running' | 'paused';

export class RecordingEventQueue implements IRecordingEventQueue {
    private state: QueueState;
    private bufferOffset: number;
    private readonly lastEvents: Denque<DataRecordingEvent>;
    private readonly bufferQueue: Denque<Uint8Array>;
    private readonly options: IRecordingEventQueueOptions;
    private sendBufferInterval?: ReturnType<typeof setInterval>;

    constructor(options: IRecordingEventQueueOptions) {
        this.options = options;
        this.bufferOffset = 0;
        this.sendBufferInterval = null;
        this.state = 'inactive';
        this.lastEvents = new Denque<DataRecordingEvent>();
        this.bufferQueue = new Denque<Uint8Array>();
        this.bufferQueue.push(new Uint8Array(1024));
        this.bufferQueue.push(new Uint8Array(1024));
        this.bufferQueue.push(new Uint8Array(1024));
        this.bufferQueue.push(new Uint8Array(1024));
        this.bufferQueue.push(new Uint8Array(1024));
        this.bufferQueue.push(new Uint8Array(1024));
        this.bufferQueue.push(new Uint8Array(1024));
        this.bufferQueue.push(new Uint8Array(1024));
    }

    append(event: RecordingEvent): void {
        debugLog?.log(`append: ${event.type}`);

        let sendImmediately = false;
        switch (event.type) {
        case RecordingEventType.Pause:
            if (this.state === 'paused')
                return;

            this.state = "paused";
            sendImmediately = true;
            break;
        case RecordingEventType.Resume:
            if (this.state === "running")
                return;
            break;
        case RecordingEventType.Data:
            if (this.state === "paused") {
                const queueLength = this.lastEvents.push(event as DataRecordingEvent);
                if (queueLength > 20) {
                    this.lastEvents.shift();
                }
                return;
            }
            break;
        }

        if (event.type == RecordingEventType.Resume) {
            this.appendInternal(event, this.lastEvents.length == 0);

            if (this.lastEvents.length) {
                while (this.lastEvents.length > 3) { // keep 3 last events on resume
                    this.lastEvents.shift();
                }
                while (this.lastEvents.length > 1) {
                    const lastEvent = this.lastEvents.shift();
                    this.appendInternal(lastEvent, false);
                }
                this.appendInternal(this.lastEvents.shift(), true);
            }

            this.state = "running";
        }
        else {
            if (this.lastEvents.length) {
                while (this.lastEvents.length > 1) {
                    const lastEvent = this.lastEvents.shift();
                    this.appendInternal(lastEvent, false);
                }
                this.appendInternal(this.lastEvents.shift(), false);
            }

            this.appendInternal(event, sendImmediately);
            if (!sendImmediately) {
                this.ensureSentByTimeout();
            }
        }
    }

    public async flushAsync(): Promise<void> {
        const origState = this.state;
        this.state = 'inactive';

        debugLog?.log(`flushAsync`);

        this.lastEvents.clear();

        if (origState == 'running') {
            await this.sendTopmostBuffer();
        }
        else {
            this.bufferOffset = 0;
        }
    }

    private appendInternal(event: RecordingEvent, sendImmediately: boolean): void {
        if (this.bufferQueue.length == 0) {
            this.bufferQueue.push(new Uint8Array(1024));
        }
        let commandLength = event.serialize(this.bufferQueue.peekFront(), this.bufferOffset);
        if (commandLength > 0)         {
            this.bufferOffset += commandLength;
        }
        if (sendImmediately || commandLength < 0) {
            (async () => await this.sendTopmostBuffer())();
        }
        if (commandLength < 0) {
            commandLength = event.serialize(this.bufferQueue.peekFront(), this.bufferOffset);
            if (commandLength > 0)         {
                this.bufferOffset += commandLength;
            }
            if (sendImmediately) {
                (async () => await this.sendTopmostBuffer())();
            }
        }

        if (this.bufferOffset >= this.options.minChunkSize) {
            (async () => await this.sendTopmostBuffer())();
        } else {
            debugLog?.log(`enqueue: ${this.bufferOffset} byte(s) were left in buffer`);
        }
    }

    private ensureSentByTimeout() {
        if (this.state === "paused")
            return;

        if (this.sendBufferInterval === null) {
            this.sendBufferInterval = setInterval(() => {
                const currentDataLength = this.bufferOffset;
                if (currentDataLength == 0)
                    return;

                debugLog?.log(`ensureSentByTimeout: send timeout is fired, sending buffer with ${this.bufferOffset} data bytes`);
                const _ = this.sendTopmostBuffer();
            }, this.options.maxFillBufferTimeMs);
        }
    }

    private async sendTopmostBuffer(): Promise<void> {
        const currentDataLength = this.bufferOffset;
        if (currentDataLength == 0)
            return;

        this.bufferOffset = 0;
        const buffer = await this.sendBufferAsync(this.bufferQueue.shift(), currentDataLength);
        this.bufferQueue.push(buffer);
    }

    private async sendBufferAsync(buffer: Uint8Array, length: number): Promise<Uint8Array> {
        if (length === 0) {
            debugLog?.log(`sendBufferAsync: buffer is empty`);
            return buffer;
        }
        const packetBytes = buffer.subarray(0, length);
        try {
            await this.options.sendAsync(packetBytes);
            debugLog?.log(`sendBufferAsync: ${length} bytes sent`);
        }
        catch (error) {
            errorLog?.log(`sendBufferAsync: unhandled error:`, error);
        }
        return buffer;
    }
}
