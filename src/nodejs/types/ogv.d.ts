interface Demuxer {
    init(callback: () => void): void;
    flush(callback: () => void): void;
    /** noop */
    close(): void;
    /**
     * Process previously queued data into packets.
     *
     * 'more' parameter to callback function is 'true' if there
     * are more packets to be processed in the queued data,
     * or 'false' if there aren't.
     */
    process(callback: (more: any) => void): void;
    /** Queue up some data for later processing */
    receiveInput(data: ArrayBuffer, callback: () => void): void;
    dequeueAudioPacket(callback: (packet: ArrayBuffer,
        /** discardPadding is in nanoseconds negative value trims from beginning, positive value trims from end */
        discardPadding: number) => void): void;
    dequeueVideoPacket(callback: (packet: ArrayBuffer) => void): void;
    /** Returns the offset of the relevant keyframe or other position just before the given presentation timestamp */
    getKeypointOffset(timeSeconds: number, callback: (offset: number) => void): void;
    /**
     * Initiate seek to the nearest keyframe or other position just before
     * the given presentation timestamp. This may trigger seek requests, and
     * it may take some time before processing returns more packets
    */
    seekToKeypoint(timeSeconds: number, callback: (isSeekingInitiated: boolean) => void): void;
    loadedMetadata: boolean;
    audioReady: boolean;
    seekable: boolean;
    hasVideo: boolean;
    hasAudio: boolean | string | 'opus';
    frameReady: boolean;
    frameTimestamp: number;
    duration: number;
    audioTimestamp: number;
    keyframeTimestamp: number;
    nextKeyframeTimestamp: number;
    /** CPU time spent in the demuxer. */
    cpuTime: number;
    /** Are we in the middle of an asynchronous processing operation? */
    processing: boolean;
    videoCodec?: string;
    audioCodec?: string;
    onseek: (offset: number) => void;
    audioPackets: any[];
}

interface Decoder {
    loadedMetadata: boolean;
    audioFormat?: { channels: number, rate: number; };
    /** Last-decoded audio packet */
    audioBuffer?: Float32Array[];
    /** CPU time spent in the decoder. */
    cpuTime: number;
    /** Are we in the middle of an asynchronous processing operation? */
    processing: boolean;
    init(callback: () => void): void;
    /** Process a header packet */
    processHeader(data: ArrayBuffer, callback: (elapsed: number) => void): void;
    /** Decode the given audio data packet; fills out the audioBuffer property on success */
    processAudio(data: ArrayBuffer, callback: (elapsed: number) => void): void;
    /** noop */
    close(): void;
}

declare module "ogv" {
    interface OGVPlayerOptions {
        /** base URL for additional resources, such as codec libraries */
        base?: string;
        /** Running the codec in a worker thread */
        worker?: boolean;
        /** Experimental SIMD mode, if built. */
        simd?: boolean;
        /** Experimental pthreads multithreading mode, if built. */
        threading?: boolean;
        debug?: boolean;
        debugFilter?: RegExp;
        video?: HTMLVideoElement | boolean;
        /** Allows replacement StreamFile instance with a compatible implementation */
        stream?: StreamFileBase;
        /** pre-created AudioContext */
        audioContext?: AudioContext;
        /** pre-created output node */
        audioDestination?: MediaStreamAudioDestinationNode;
        /** Allows setting a custom backend for audioFeeder */
        audioBackendFactory?: any;
    }

    class OGVPlayer extends HTMLAudioElement {
        constructor(options?: OGVPlayerOptions);
    }
    class OGVLoader {
        constructor();
        base: string;
        urlForClass(className: string): string;
        urlForScript(scriptName: string): string;
        loadClass(className: string, callback: Function, options?: any);
    }
    class OGVCompat {
        hasWebAudio(): boolean;
        hasWebAssembly(): boolean;
        supported(component: string): boolean;
    }

    interface StreamFileBase {
        buffering: boolean;
        seeking: boolean;
        seekable: boolean;
        /** Might contain 'x-content-duration' */
        headers: any;
        /** Is the read head at the end of the file? */
        readonly eof: boolean;
        /** Should be -1 if undefined */
        length: number;
        waiting: boolean;
        /** Abort any currently running downloads and operations.*/
        abort(): void;
        /**
         * Seek the read position to a new location in the file, asynchronously.
         * After succesful completion, reads will continue at the new offset.
         * May fail due to network problems, invalid input, or bad state.
         * @param {number} offset - target byte offset from beginning of file
         * @returns {Promise<void>} - resolved when ready to read at the new position
         */
        seek(offset: number): Promise<void>;
        /**
         * Return an array of byte ranges that are buffered.
         * Each range is a two-element array of start and end.
         * @returns {Array<Array<number>>}
         */
        getBufferedRanges(): Array<Array<number>>;
        /**
         * Read up to the requested number of bytes, or until end of file is reached,
         * and advance the read head.
         *
         * May wait on network activity if data is not yet available.
         *
         * @param {number} nbytes - max number of bytes to read
         * @returns {Promise<ArrayBuffer>} - between 0 and nbytes of data, inclusive
         */
        read(nbytes: number): Promise<ArrayBuffer>;
        /**
         * Open the file, get metadata, and start buffering some data.
         * On success, loaded will become true, headers may be filled out,
         * and length may be available.
         */
        load(): Promise<void>;
    }

    interface StreamFile extends StreamFileBase {
        url: string;
        loaded: boolean;
        loading: boolean;
        progressive: boolean;
        /** Byte offset of the read head */
        readonly offset: number;

        /**
         * Read up to the requested number of bytes, or however much is available
         * in the buffer until the next empty segment, and advance the read head.
         *
         * Returns immediately.
         *
         * @param {number} nbytes - max number of bytes to read
         * @returns {ArrayBuffer} - between 0 and nbytes of data, inclusive
         */
        readSync(nbytes: number): ArrayBuffer;

        /**
         * Read bytes into destination array until out of buffer or space,
         * and advance the read head.
         *
         * Returns immediately.
         *
         * @param {dest} Uint8Array - destination byte array
         * @returns {number} - count of actual bytes read
         */
        readBytes(dest: Uint8Array): number;

        /**
         * Wait until the given number of bytes are available to read, or end of file.
         * @param {number} nbytes - max bytes to wait for
         * @returns {Promise<number>} - resolved with available byte count when ready
         */
        buffer(nbytes: number): Promise<number>;


        /**
         * Number of bytes available to read immediately from the current offset.
         * This is the max number of bytes that can be returned from a read() call.
         * @returns {boolean}
         */
        bytesAvailable(max: number): boolean;
    }
}