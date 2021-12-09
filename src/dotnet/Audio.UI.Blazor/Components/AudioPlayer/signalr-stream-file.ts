import { StreamFileBase } from 'ogv';
import Denque from 'denque';

interface Chunk {
    offset: number;
    data: Uint8Array;
}

/** Adapter of StreamFile from the stream-file package for the ogv.js */
export class SignalrStreamFile implements StreamFileBase {
    public seekable: boolean;
    public seeking: boolean;
    public headers: any;
    public isAborted: boolean;
    public waiting: boolean;
    private _chunks: Denque<Chunk>;
    private _readCursorChunk: number;
    private _readCursorChunkOffset: number;
    private _eofChunk?: Chunk;
    private readonly _debugMode: boolean;

    public get eof(): boolean {
        if (this._eofChunk === null)
            return false;
        if (this._chunks.length === 0)
            return false;
        const currentChunk = this._chunks.peekAt(this._readCursorChunk);
        if (this._eofChunk !== currentChunk)
            return false;
        return this._readCursorChunkOffset === currentChunk.data.length;
    }

    public get length(): number {
        const lastChunk = this._chunks.peekBack();
        if (lastChunk === undefined)
            return -1;
        return lastChunk.offset + lastChunk.data.byteLength;
    }
    public get buffering(): boolean {
        //TODO: adjust this
        return this._chunks.isEmpty();
    }

    constructor(debugMode: boolean) {
        this.seekable = false;
        this.seeking = false;
        this.headers = {};
        this._chunks = new Denque<Chunk>();
        this._eofChunk = null;
        this._readCursorChunk = 0;
        this._readCursorChunkOffset = 0;
        this.waiting = true;
        this.isAborted = false;
        this._debugMode = debugMode;
    }

    public write(data: Uint8Array): void {
        const lastChunk = this._chunks.peekBack();
        const chunk: Chunk = {
            offset: lastChunk === undefined ? 0 : lastChunk.offset + lastChunk.data.byteLength + 1,
            data: data
        };
        this._chunks.push(chunk);
        // TODO: add cleanup
    }

    public endOfStream() {
        this._eofChunk = this._chunks.peekBack();
    }

    public abort(): void {
        this.isAborted = true;
        this.endOfStream();
    }

    public getBufferedRanges(): number[][] {
        let ranges = [];
        this._chunks.toArray().forEach(c => {
            ranges.push([c.offset, c.offset + c.data.byteLength]);
        });
        return ranges;
    }

    public async read(nbytes: number): Promise<ArrayBuffer> {
        if (this._debugMode) {
            this.log(`read(${nbytes})`);
        }
        const buffer = new Uint8Array(nbytes);
        let bufferCursor = 0;
        let remaining = nbytes;
        while (!this.eof && !this.isAborted && remaining > 0) {
            let waitForData = false;

            if (this._chunks.length === 0) {
                waitForData = true;
            }
            else {
                const chunk = this._chunks.peekAt(this._readCursorChunk);
                // we are at the end of current chunk, move to the next one
                if (this._readCursorChunkOffset === chunk.data.length) {
                    if (this._readCursorChunk + 1 < this._chunks.length) {
                        this._readCursorChunk++;
                        this._readCursorChunkOffset = 0;
                    }
                    else {
                        waitForData = true;
                    }
                }
                else {
                    // we can read all data from the current chunk, read then exit
                    if (remaining <= chunk.data.length) {
                        buffer.set(chunk.data.subarray(this._readCursorChunkOffset, remaining), bufferCursor);
                        this._readCursorChunkOffset += remaining;
                        bufferCursor += remaining;
                        remaining = 0;
                    }
                    // we cannot read all data from the current chunk,
                    // so we need to read to the end of the current chunk
                    // then go to the next chunk (in next iteration of the loop)
                    else if (remaining > chunk.data.length) {
                        buffer.set(chunk.data.subarray(this._readCursorChunkOffset, this._readCursorChunkOffset + remaining), bufferCursor);
                        bufferCursor += chunk.data.length;
                        remaining -= chunk.data.length;
                        this._readCursorChunkOffset = chunk.data.length;
                    }
                }
            }
            if (waitForData) {
                this.waiting = true;
                await this.delay(25);
            }
        }
        if (!this.isAborted && !this.eof) {
            console.assert(bufferCursor === buffer.length, "Read must return requested number of bytes.");
        }
        this.waiting = false;
        return buffer.subarray(0, bufferCursor);
    }

    public load(): Promise<void> {
        return Promise.resolve();
    }

    public seek(offset: number): Promise<void> {
        throw new Error("Method shouldn't be called. Implementation isn't seekable.");
    }

    private delay(ms: number): Promise<void> {
        return new Promise<void>(resolve => setTimeout(resolve, ms));
    }

    private log(message: string) {
        console.debug(`[${new Date(Date.now()).toISOString()}] SignalrStreamFile: ${message}`);
    }
}
