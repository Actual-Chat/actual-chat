import Denque from "denque";

const LogScope = 'SendingQueue';

export interface ISendingQueue {
    pause(): Promise<void>;
    resume(): Promise<void>;
    enqueue(data: Uint8Array): void;
    flushAsync(): Promise<void>;
    // TODO: remove unused code with sequenceNumbers
    resendAsync(sequenceNumber: number): Promise<void>;
}

export interface ISendingQueueCleaningStrategy {
    cleanAsync(chunks: Map<number, Uint8Array>, currentSequenceNumber: number): Promise<void>;
}

export interface ISendingQueueOptions {
    chunkSize: number;
    minChunkSize: number;
    maxFillBufferTimeMs: number;
    sendAsync: (data: Uint8Array) => Promise<void>;
    cleaningStrategy: ISendingQueueCleaningStrategy;
    debugMode: boolean;
}

export class SendingQueue implements ISendingQueue {
    private _state: 'running' | 'paused';
    private _buffer: Uint8Array;
    private _bufferOffset: number;
    private _seqNum: number;
    private readonly _lastBlocks: Denque<Uint8Array>;
    private readonly _chunks: Map<number, Uint8Array>;
    private readonly _options: ISendingQueueOptions;
    private _sendBufferTimeout?: ReturnType<typeof setTimeout>;

    constructor(options: ISendingQueueOptions) {
        this._options = options;
        this._buffer = new Uint8Array(this._options.chunkSize);
        this._bufferOffset = 0;
        this._seqNum = 0;
        this._sendBufferTimeout = null;
        this._chunks = new Map<number, Uint8Array>();
        this._state = 'running';
        this._lastBlocks = new Denque<Uint8Array>();
    }

    public async pause(): Promise<void> {
        this._state = "paused";

        if (this._bufferOffset > 0) {
            await this.sendBufferAsync(true);
        }
        this._buffer.set([0, 0, 0, 0], 0);
        this._bufferOffset = 4;
        await this.sendBufferAsync(true);
    }

    public async resume(): Promise<void> {
        if (this._state === "running")
            return;

        this._buffer.set([1, 1, 1, 1], 0);
        this._bufferOffset = 4;
        await this.sendBufferAsync(true);

        while (this._lastBlocks.length) {
            const block = this._lastBlocks.shift();
            if (block.length + this._bufferOffset >= this._buffer.length) {
                await this.sendBufferAsync(true);
            }
            this._buffer.set(block, this._bufferOffset);
            this._bufferOffset += block.length;
        }
        await this.sendBufferAsync(true);
        this._state = "running";
    }

    public enqueue(data: Uint8Array): void {
        if (this._options.debugMode) {
            this.log(`enqueue: ${data.length} byte(s)`);
        }

        if (this._state === "paused") {
            const queueLength = this._lastBlocks.push(data);
            if (queueLength > 10) {
                this._lastBlocks.shift();
            }
            return;
        }

        const chunkSize = this._options.chunkSize;
        let freeBufferLength = chunkSize - this._bufferOffset;
        while (data.length >= freeBufferLength) {
            const dataPrefix = data.subarray(0, freeBufferLength);
            data = data.subarray(freeBufferLength);
            this._buffer.set(dataPrefix, this._bufferOffset);
            this._bufferOffset += freeBufferLength;
            const _ = this.sendBufferAsync();
            freeBufferLength = chunkSize - this._bufferOffset; // Actually always chunkSize
        }
        if (data.length > 0) {
            // We know for sure here that data fits into the buffer
            this._buffer.set(data, this._bufferOffset);
            this._bufferOffset += data.length;
        }

        if (this._bufferOffset >= this._options.minChunkSize) {
            const _ = this.sendBufferAsync();
        } else {
            this.log(`enqueue: ${this._bufferOffset} byte(s) were left in buffer`);
        }
        this.ensureSendByTimeout();
    }

    public async flushAsync(): Promise<void> {
        if (this._options.debugMode) {
            this.log(`flushAsync is called, seqNum: ${this._seqNum}`);
        }
        await this.sendBufferAsync();
    }

    public async resendAsync(sequenceNumber: number): Promise<void> {
        const chunk = this._chunks.get(sequenceNumber);
        if (chunk === undefined) {
            throw new Error(`sequenceNumber ${sequenceNumber} is not found`);
        }
        await this._options.sendAsync(chunk);
    }

    private ensureSendByTimeout() {
        if (this._sendBufferTimeout === null) {
            this._sendBufferTimeout = setTimeout(() => {
                if (this._options.debugMode) {
                    this.log(`Send timeout is fired, sending buffer with ${this._bufferOffset} data bytes, seqNum: ${this._seqNum}.`);
                }
                const _ = this.sendBufferAsync();
            }, this._options.maxFillBufferTimeMs);
        }
    }

    private clearSendTimeout() {
        if (this._sendBufferTimeout !== null) {
            clearTimeout(this._sendBufferTimeout);
            this._sendBufferTimeout = null;
        }
    }

    private async sendBufferAsync(ignorePause: boolean = false): Promise<void> {
        this.clearSendTimeout();

        if (this._state === "paused" && !ignorePause)
            return;

        const bufferLength = this._bufferOffset;
        const seqNum = this._seqNum;
        if (bufferLength === 0) {
            if (this._options.debugMode) {
                this.log(`Buffer for seqNum #${seqNum} is empty.`);
            }
            return;
        }
        const packetBytes = new Uint8Array(bufferLength + 4);
        packetBytes[0] = seqNum & 0xFF;
        packetBytes[1] = (seqNum >> 8) & 0xFF;
        packetBytes[2] = (seqNum >> 16) & 0xFF;
        packetBytes[3] = (seqNum >> 24) & 0xFF;
        packetBytes.set(this._buffer.subarray(0, bufferLength), 4);
        this._chunks.set(seqNum, packetBytes);
        // increment through boundary of uint32.max (ex: 0xFFFFFFFF + 1 == 0)
        this._seqNum = (seqNum + 1) >>> 0;
        this._buffer = new Uint8Array(this._options.chunkSize);
        this._bufferOffset = 0;
        try {
            await this._options.sendAsync(packetBytes);
            if (this._options.debugMode) {
                this.log(`Sent ${bufferLength} data bytes, seqNum: ${seqNum}`);
            }
            let _ = this._options.cleaningStrategy.cleanAsync(this._chunks, seqNum);
        }
        catch (error) {
            if (this._options.debugMode) {
                this.logError(`Couldn't send ${bufferLength - 4} (${bufferLength}) data bytes, seqNum: ${seqNum}, error: ${error}`);
            }
        }
    }

    private log(message: string) {
        console.debug(`${LogScope}: ${message}`);
    }

    private logError(message: string) {
        console.error(`${LogScope}: ${message}`);
    }
}

export class TimeoutCleaningStrategy implements ISendingQueueCleaningStrategy {

    private _seqNums: Map<number, number>;
    private _lastCleanRunTimeMs: number;
    private _maxTimeInQueueMs: number;

    constructor(maxTimeInQueueMs: number) {
        this._seqNums = new Map<number, number>();
        this._lastCleanRunTimeMs = Date.now();
        this._maxTimeInQueueMs = maxTimeInQueueMs;
    }

    public cleanAsync(chunks: Map<number, Uint8Array>, currentSequenceNumber: number): Promise<void> {
        const now = Date.now();
        this._seqNums.set(currentSequenceNumber, now);
        const elapsedMs = now - this._lastCleanRunTimeMs;
        if (elapsedMs < this._maxTimeInQueueMs) {
            return Promise.resolve();
        }
        this._lastCleanRunTimeMs = now;
        return new Promise<void>((resolve, _reject) => {
            this._seqNums.forEach((v, k) => {
                if ((now - v) > this._maxTimeInQueueMs) {
                    this._seqNums.delete(k);
                    chunks.delete(k);
                }
            });
            resolve();
        });
    }
}

export default SendingQueue;
