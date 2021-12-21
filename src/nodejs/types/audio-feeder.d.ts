declare module "audio-feeder" {

    interface WebAudioBackend { /* internal api, we don't use or override this */ }

    interface AudioFeederOptions {
        /** Size of output buffers in samples, as a hint for latency/scheduling */
        bufferSize: number;
        audioContext?: AudioContext;
        output?: AudioDestinationNode;
        backendFactory?: (numChannels: number, sampleRate: number, options: AudioFeederOptions) => WebAudioBackend;

    }

    interface PlaybackState {
        samplesQueued: number;
        outputPlaybackPosition: number;
        playbackPosition: number;
        dropped: number;
        delayed: number;
    }

    export default class AudioFeeder {
        constructor(options?: AudioFeederOptions);
        /** Sample rate in Hz */
        readonly rate: number;
        /** Number of output channels */
        readonly channels: number;
        /** Size of output buffers in samples, as a hint for latency/scheduling */
        readonly bufferSize: number;
        /** Actual output sample rate in Hz, as provided by the backend. */
        readonly targetRate: number;
        /** Duration of remaining queued data, in seconds. */
        readonly durationBuffered: number;
        /**
         * Duration of the minimum buffer size, in seconds.
         * If the amount of buffered data falls below this,
         * caller will receive a synchronous 'starved' event
         * with a last chance to buffer more data.
         */
        readonly bufferDuration: number;
        /**
         * Current playback position, in seconds, in input time (i.e. pre tempo change)
         * This compensates for drops and underruns, and is suitable for a/v sync.
         */
        readonly playbackPosition: number;
        /**
         * Current playback position, in seconds, in output time (i.e. post tempo change)
         * Also compensates for drops and underruns, and is suitable for a/v sync.
         */
        readonly outputPlaybackPosition: number;
        /** Is the AudioFeeder class supported in this browser? */
        static isSupported(): boolean;
        /**
         * Duration of remaining data at which a 'bufferlow' event will be
         * triggered, in seconds.
         *
         * This defaults to twice bufferDuration, but can be overridden.
         */
        bufferThreshold: number;
        /** Volume multiplier, defaults to 1.0. */
        volume: number;
        /**
         * Tempo multiplier, defaults to 1.0.
         */
        tempo: number;

        /**
         * Is the feeder currently set to mute output?
         * When muted, this overrides the volume property.
         *
         */
        muted: boolean;
        /**
         * Force initialization of the default Web Audio API context, if applicable.
         *
         * Some browsers (such as mobile Safari) disable audio output unless
         * first triggered from a UI event handler; call this method as a hint
         * that you will be starting up an AudioFeeder soon but won't have data
         * for it until a later callback.
         *
         * @returns {AudioContext|null} - initialized AudioContext instance, if applicable
         */
        static initSharedAudioContext(): AudioContext | null;
        /**
         * Start setting up for output with the given channel count and sample rate.
         * Audio data you provide will be resampled if necessary to whatever the
         * backend actually supports.
         *
         * @param {number} numChannels - requested number of channels (output may differ)
         * @param {number} sampleRate - requested sample rate in Hz (output may differ)
         */
        init(numChannels: number, sampleRate: number);
        /**
         * Queue up some audio data for playback.
         * @param {Float32Array[]} sampleData - input data to queue up for playback
         */
        bufferData(sampleData: Float32Array[]);
        /**
         * Get an object with information about the current playback state.
         * Can throw something like `TypeError: Cannot read properties of undefined (reading 'out_time') `
         */
        getPlaybackState(): PlaybackState;
        /** Asynchronous callback when we're running low on buffered data. */
        onbufferlow?: () => void;
        /** Synchronous callback when we find we're out of buffered data. */
        onstarved?: () => void;
        /** Checks if audio system is ready and calls the callback when ready  to begin playback. */
        waitUntilReady(callback: () => void);
        /**
         * Start/continue playback as soon as possible.
         * You should buffer some audio ahead of time to avoid immediately
         * running into starvation.
         */
        start(): void;
        /**
         * Stop/pause playback as soon as possible.
         * Audio that has been buffered but not yet sent to the device will
         * remain buffered, and can be continued with another call to start().
         */
        stop(): void;
        /**
         * Flush any queued data out of the system.
         * Can throw something like `TypeError: Cannot read properties of undefined (reading 'out_time') `
         */
        flush(): void;
        /** Close out the audio channel. The AudioFeeder instance will no longer be usable after closing. */
        close(): void;
    }
}