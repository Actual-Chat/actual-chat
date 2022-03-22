export class AudioRingBuffer {
    private readonly channelCount: number;
    private readonly bufferSize: number;
    private readonly channelBuffers: Float32Array[];
    private readIndex: number;
    private writeIndex: number;

    constructor(bufferSize: number, channelCount: number) {
        this.readIndex = 0;
        this.writeIndex = 0;
        this._framesAvailable = 0;

        if (bufferSize < 1024) {
            throw new Error(`Min. buffer size is 1024, but specified bufferSize is ${bufferSize}.`);
        }
        this.channelCount = channelCount;
        this.bufferSize = bufferSize;
        this.channelBuffers = [];
        for (let channel = 0; channel < this.channelCount; channel++) {
            this.channelBuffers[channel] = new Float32Array(bufferSize);
        }
    }

    private _framesAvailable: number;
    public get framesAvailable() {
        return this._framesAvailable;
    }

    public push(multiChannelData: Float32Array[]): void {
        if (multiChannelData == null) {
            throw new Error(`multiChannelData is null or undefined.`);
        }
        if (multiChannelData.length == 0) {
            throw new Error(`multiChannelData is empty.`);
        }
        const sourceLength = multiChannelData[0].length;
        if (sourceLength > this.bufferSize / 2) {
            throw new Error(`multiChannelData should not contain frames more than half of the bufferSize, length of multichannelData=${sourceLength}, bufferSize=${this.bufferSize}.`);
        }

        const writeIndex = this.writeIndex;
        const endWriteIndex = (writeIndex + sourceLength) % this.bufferSize;
        if (endWriteIndex < writeIndex && endWriteIndex > this.readIndex) {
            throw new Error(`Buffer can't be overwritten.`);
        }

        for (let channel = 0; channel < this.channelCount; channel++) {
            if (endWriteIndex >= writeIndex) {
                this.channelBuffers[channel].set(multiChannelData[channel], writeIndex);
            }
            else {
                const firstPartEnd = sourceLength - endWriteIndex;
                const firstPart = multiChannelData[channel].subarray(0, firstPartEnd);
                const secondPart = multiChannelData[channel].subarray(firstPartEnd);
                this.channelBuffers[channel].set(firstPart, writeIndex);
                this.channelBuffers[channel].set(secondPart, 0);
            }
        }
        this.writeIndex = endWriteIndex;
        this._framesAvailable += sourceLength;
    }

    public pull(multiChannelData: Float32Array[]): boolean {
        if (multiChannelData == null) {
            throw new Error(`multiChannelData is null or undefined.`);
        }
        if (multiChannelData.length == 0) {
            throw new Error(`multiChannelData is empty.`);
        }
        const destinationLength = multiChannelData[0].length;
        if (destinationLength > this.bufferSize / 2) {
            throw new Error(`multiChannelData should not have length longer than half of the bufferSize, length of multichannelData=${destinationLength}, bufferSize=${this.bufferSize}.`);
        }
        if (this._framesAvailable === 0) {
            return false;
        }
        if (this._framesAvailable < destinationLength) {
            return false;
        }

        const readIndex = this.readIndex;
        const endReadIndex = (readIndex + destinationLength) % this.bufferSize;

        for (let channel = 0; channel < this.channelCount; channel++) {
            if (endReadIndex >= readIndex) {
                multiChannelData[channel].set(this.channelBuffers[channel].subarray(readIndex, endReadIndex));
            }
            else {
                const firstPartEnd = destinationLength - endReadIndex;
                const firstPart = this.channelBuffers[channel].subarray(readIndex);
                const secondPart = this.channelBuffers[channel].subarray(0, endReadIndex);
                multiChannelData[channel].set(firstPart, 0);
                multiChannelData[channel].set(secondPart, firstPartEnd);
            }
        }

        this.readIndex = endReadIndex;
        this._framesAvailable -= destinationLength;

        return true;
    }
}
