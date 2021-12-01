export class SendingQueue {
    private _buffer: Uint8Array;
    private _bufferLength: number;
    private _seqNum: number;
    private readonly _chunks: Map<number, Uint8Array>;
    private readonly _options: ISendingQueueOptions;
    private _sendBufferTimeout?: ReturnType<typeof setTimeout>;

    constructor(options: ISendingQueueOptions) {
        this._options = options;
        this._buffer = new Uint8Array(this._options.chunkSize * 2);
        this._bufferLength = 0;
        this._seqNum = 0;
        this._sendBufferTimeout = null;
        this._chunks = new Map<number, Uint8Array>();
    }

    public enqueue(data: Uint8Array) {
        if (this._options.debugMode) {
            this.log(`enqueue: ${data.length} byte(s)`);
        }

        const chunkSize = this._options.chunkSize;

        while (true) {
            let freeBufferLength = chunkSize - this._bufferLength;
            if (data.length < freeBufferLength)
                break;
            let dataPrefix = data.subarray(0, freeBufferLength);
            data = data.subarray(freeBufferLength)
            this._buffer.set(dataPrefix, this._bufferLength);
            this._bufferLength += freeBufferLength;
            let _ = this.sendBufferAsync();
        }
        if (data.length > 0) {
            // We know for sure here that data fits into the buffer
            this._buffer.set(data, this._bufferLength);
            this._bufferLength += data.length;
        }

        if (this._bufferLength >= this._options.minChunkSize) {
            let _ = this.sendBufferAsync();
        } else {
            this.log(`enqueue: ${this._bufferLength} byte(s) were left in buffer`);
        }
        this.ensureSendTimeout();
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
                    this.log(`Send timeout is fired, sending buffer with ${this._bufferLength} data bytes, seqNum: ${this._seqNum}.`);
                }
                let _ = this.sendBufferAsync();
            }, this._options.maxFillBufferTimeMs);
        }
    }

    private clearSendTimeout() {
        if (this._sendBufferTimeout !== null) {
            clearTimeout(this._sendBufferTimeout);
            this._sendBufferTimeout = null;
        }
    }

    private async sendBufferAsync(): Promise<void> {
        this.clearSendTimeout();
        const bufferLength = this._bufferLength;
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
        this._buffer = new Uint8Array(this._options.chunkSize * 2);
        this._bufferLength = 0;
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
}

export interface ISendingQueueOptions {
    chunkSize: number;
    minChunkSize: number;
    maxFillBufferTimeMs: number;
    sendAsync: (data: Uint8Array) => Promise<void>;
    cleaningStrategy: ISendingQueueCleaningStrategy;
    debugMode: boolean;
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
