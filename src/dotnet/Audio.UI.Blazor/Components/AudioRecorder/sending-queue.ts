export class SendingQueue {

    private _buffer: Uint8Array;
    private _bufferCursor: number;
    private _currentSequenceNumber: number;
    private readonly _chunks: Map<number, Uint8Array>;
    private readonly _options: ISendingQueueOptions;
    private _sendBufferTimeout?: ReturnType<typeof setTimeout>;

    constructor(options: ISendingQueueOptions) {
        this._options = options;
        this._buffer = new Uint8Array(this._options.maxChunkSize);
        this._bufferCursor = 0;
        this._currentSequenceNumber = 0;
        this._sendBufferTimeout = null;
        this._chunks = new Map<number, Uint8Array>();
    }

    public enqueue(data: Uint8Array) {

        if (this._options.debugMode) {
            this.log(`Enqueue data size ${data.length}.`);
        }

        if ((this._bufferCursor + data.length) < this._buffer.length) {
            this._buffer.set(data, this._bufferCursor);
            this._bufferCursor += data.length;
        }
        else {
            const chunkSize = this._buffer.length;
            const padding = this._bufferCursor;
            if (padding !== 0) {
                const paddingSlice = data.subarray(0, chunkSize - padding);
                this._buffer.set(paddingSlice, padding);
                this._bufferCursor += paddingSlice.length;
                console.assert(this._bufferCursor == this._buffer.length, "wrong work with padding and buffer length");
                if (this._options.debugMode) {
                    this.log(`Sending packet with padding ${padding}, added data len was ${data.length}, new data len is ${data.subarray(paddingSlice.length).length}`);
                }
                this.sendBuffer();
                data = data.subarray(paddingSlice.length);
            }
            const chunkNum = Math.floor(data.length / chunkSize);
            for (let i = 0; i < chunkNum; ++i) {
                const slice = data.subarray(chunkSize * i, chunkSize * (i + 1));
                this._buffer.set(slice, this._bufferCursor);
                this._bufferCursor += slice.length;
                console.assert(this._bufferCursor == this._buffer.length, "wrong work with buffer length");
                if (this._options.debugMode) {
                    this.log(`Sending packet ${i} [${chunkSize * i}, ${chunkSize * (i + 1)}).`);
                }
                this.sendBuffer();
            }
            if (data.length % chunkSize !== 0) {
                const remaining = data.subarray(chunkNum * chunkSize);
                console.assert(data.length % chunkSize == remaining.length, "wrong work with remaining len");
                this._buffer.set(remaining, this._bufferCursor);
                this._bufferCursor += remaining.length;
                if (this._options.debugMode) {
                    this.log(`Remaining ${this._bufferCursor} [${chunkNum * chunkSize}, ${data.length}) data bytes in buffer`);
                }
            }
        }
        this.ensureSendTimeout();
    }

    public async resendAsync(sequenceNumber: number): Promise<void> {
        const chunk = this._chunks.get(sequenceNumber);
        if (chunk === undefined) {
            throw new Error(`sequenceNumber ${sequenceNumber} is not found`);
        }
        await this._options.sendAsync(chunk);
    }

    private log(message: string) {
        console.debug(`[${new Date(Date.now()).toISOString()}] SendingQueue: ${message}`);
    }

    private logError(message: string) {
        console.error(`[${new Date(Date.now()).toISOString()}] SendingQueue: ${message}`);
    }

    private ensureSendTimeout() {
        if (this._sendBufferTimeout === null) {
            this._sendBufferTimeout = setTimeout(() => {
                if (this._options.debugMode) {
                    this.log(`Send timeout is fired, sending buffer with ${this._bufferCursor} data bytes, seqNum: ${this._currentSequenceNumber}.`);
                }
                this.sendBuffer();
            }, this._options.maxFillBufferTimeMs);
        }
    }

    private clearSendTimeout() {
        if (this._sendBufferTimeout !== null) {
            clearTimeout(this._sendBufferTimeout);
            this._sendBufferTimeout = null;
        }
    }

    private sendBuffer() {
        this.clearSendTimeout();
        const buffLen = this._bufferCursor;
        const seqNum = this._currentSequenceNumber;
        if (buffLen === 0) {
            if (this._options.debugMode) {
                this.log(`Buffer for seqNum: ${seqNum} is empty, sending is not needed.`);
            }
            return;
        }
        const packetBytes = new Uint8Array(buffLen + 4);
        packetBytes[0] = seqNum & 0xFF;
        packetBytes[1] = (seqNum >> 8) & 0xFF;
        packetBytes[2] = (seqNum >> 16) & 0xFF;
        packetBytes[3] = (seqNum >> 24) & 0xFF;
        packetBytes.set(this._buffer.subarray(0, buffLen), 4);
        this._chunks.set(seqNum, packetBytes);
        // increment through boundary of uint32.max (ex: 0xFFFFFFFF + 1 == 0)
        this._currentSequenceNumber = (seqNum + 1) >>> 0;
        this._buffer = new Uint8Array(this._options.maxChunkSize);
        this._bufferCursor = 0;

        this._options.sendAsync(packetBytes).then(() => {
            if (this._options.debugMode) {
                this.log(`Sent ${buffLen} data bytes, seqNum: ${seqNum}`);
            }
            this._options.cleaningStrategy.cleanAsync(this._chunks, seqNum);
        }, err => {
            if (this._options.debugMode) {
                this.logError(`Couldn't send ${buffLen - 4} (${buffLen}) data bytes, seqNum: ${seqNum}, error: ${err}`);
            }
        });
    }
}

export interface ISendingQueueOptions {
    maxChunkSize: number;
    maxFillBufferTimeMs: number;
    sendAsync: (data: Uint8Array) => Promise<void>;
    debugMode: boolean;
    cleaningStrategy: ISendingQueueCleaningStrategy;
}

export interface ISendingQueueCleaningStrategy {
    cleanAsync(chunks: Map<number, Uint8Array>, currentSequenceNumber: number): Promise<void>;
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
