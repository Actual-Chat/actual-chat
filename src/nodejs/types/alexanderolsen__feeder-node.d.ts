declare module "@alexanderolsen/feeder-node" {

    interface createFeederNodeOptions {
        inputSampleRate?: number,
        /**  Stuck at 128 for `AudioWorklet`s. */
        batchSize?: 128 | 256 | 512,
        /** Number of samples to buffer before propagating to destination */
        bufferThreshold?: number,
        /** Length of RingBuffer. See ring-buffer.js for more */
        bufferLength?: number,
        /** See **resampConverterType** */
        resampConverterType?: number,
        /** location of your feeder-node.processor.js */
        pathToWorklet?: string,
        /** location of your feeder-node.worker.js */
        pathToWorker?: string,
        /** location of your libsamplerate.wasm */
        pathToWasm?: string;
    }

    interface FeederNode {
        readonly bufferLength: number;
        readonly nChannels: number;
        readonly batchSize: number;
        readonly numberOfInputs: number;
        readonly numberOfOutputs: number;
        readonly channelCount: number;

        readonly channelCountMode: any;
        readonly channelInterpretation: any;

        /** Connects FeederNode to the specific destination AudioNode */
        connect(destination: AudioDestinationNode): void;
        /** Disconnects from the currently-connected AudioNode */
        disconnect(): void;
        /**
         * Feeds raw PCM audio data to the underlying node. Any kind of TypedArray can be submitted - FeederNode
         * will automatically convert to Float32 and scale to -1 < n < 1.
         */
        feed(data: Int32Array | Int16Array | Int8Array | Uint32Array | Uint16Array | Uint8Array | Float32Array): void;

        onBackendReady: () => void;
        onBackendPlaying: () => void;
        onBackendStarved: () => void;
    }

    function createFeederNode(audioContext: AudioContext, nChannels: 1 | 2, options: createFeederNodeOptions): Promise<FeederNode>;
}
