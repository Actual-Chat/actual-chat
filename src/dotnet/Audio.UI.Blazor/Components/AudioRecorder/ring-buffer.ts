export class RingBuffer {
    private _readIndex: number;
    private _writeIndex: number;
    private _framesAvailable: number;
    private _channelCount: number;
    private _bufferSize: number;
    private _channelBuffers: Float32Array[];

    constructor(bufferSize: number, channelCount: number) {
      this._readIndex = 0;
      this._writeIndex = 0;
      this._framesAvailable = 0;
  
      this._channelCount = channelCount;
      this._bufferSize = bufferSize;
      this._channelBuffers = [];
      for (let i = 0; i < this._channelCount; ++i) {
        this._channelBuffers[i] = new Float32Array(bufferSize);
      }
    }
  
    public get framesAvailable() {
      return this._framesAvailable;
    }
  
    // TODO(AK): Refactror - make efficient !!!
    public push(multiChannelData: Float32Array[]): void {
      const sourceLength = multiChannelData[0].length;

      for (let i = 0; i < sourceLength; ++i) {
        const writeIndex = (this._writeIndex + i) % this._bufferSize;
        for (let channel = 0; channel < this._channelCount; ++channel) {
          this._channelBuffers[channel][writeIndex] = multiChannelData[channel][i];
        }
      }
  
      this._writeIndex += sourceLength;
      if (this._writeIndex >= this._bufferSize) {
        this._writeIndex = 0;
      }
  
      // For excessive frames, the buffer will be overwritten.
      this._framesAvailable += sourceLength;
      if (this._framesAvailable > this._bufferSize) {
        this._framesAvailable = this._bufferSize;
      }
    }
  
    public pull(multiChannelData: Float32Array[]): boolean {
      if (this._framesAvailable === 0) {
        return false;
      }
  
      let destinationLength = multiChannelData[0].length;
  
      for (let i = 0; i < destinationLength; ++i) {
        let readIndex = (this._readIndex + i) % this._bufferSize;
        for (let channel = 0; channel < this._channelCount; ++channel) {
          multiChannelData[channel][i] = this._channelBuffers[channel][readIndex];
        }
      }
  
      this._readIndex += destinationLength;
      if (this._readIndex >= this._bufferSize) {
        this._readIndex = 0;
      }
  
      this._framesAvailable -= destinationLength;
      if (this._framesAvailable < 0) {
        this._framesAvailable = 0;
      }
      return true;
    }
  } // class RingBuffer