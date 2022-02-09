import WebMOpusWasm from 'opus-media-recorder/WebMOpusEncoder.wasm';
import { AudioContextPool } from 'audio-context-pool';

import { EncoderResponseMessage } from './opus-media-recorder-message';
import { ProcessorOptions } from './worklets/opus-encoder-worklet-processor';
import { EncoderWorkletMessage } from './worklets/opus-encoder-worklet-message';
import { DoneMessage, EncoderMessage, InitMessage, LoadEncoderMessage } from './workers/opus-encoder-worker-message';

type WorkerState = 'inactive'|'readyToInit'|'encoding';

export class DataEvent extends Event {
    readonly data: Uint8Array;

    constructor(data: Uint8Array) {
        super('datarecorded');
        this.data = data;
    }
}

export interface OpusMediaRecorderOptions extends  MediaRecorderOptions {
    sessionId: string;
    chatId: string;
}

const SAMPLE_RATE = 48000;
const mimeType = 'audio/webm';
export class OpusMediaRecorder extends EventTarget implements MediaRecorder {
    private readonly worker: Worker;
    private readonly channelCount: number = 1;
    private readonly workerChannel: MessageChannel;

    private context: AudioContext = null;
    private workerState: WorkerState = 'inactive';
    private encoderWorklet: AudioWorkletNode = null;
    private stopResolve: () => void = null;

    public source?: MediaStreamAudioSourceNode = null;
    public stream: MediaStream;
    public readonly videoBitsPerSecond: number = NaN;
    public readonly audioBitsPerSecond: number;
    public readonly mimeType: string = mimeType;
    public readonly sessionId: string;
    public readonly chatId: string;

    public state: RecordingState = 'inactive';

    public ondatarecorded: ((ev: DataEvent) => void) | null;
    public ondataavailable: ((ev: BlobEvent) => void) | null;
    public onerror: ((ev: MediaRecorderErrorEvent) => void) | null;
    public onpause: ((ev: Event) => void) | null;
    public onresume: ((ev: Event) => void) | null;
    public onstart: ((ev: Event) => void) | null;
    public onstop: ((ev: Event) => void) | null;

    constructor(options: OpusMediaRecorderOptions) {
        super();

        this.audioBitsPerSecond = options.audioBitsPerSecond;
        this.sessionId = options.sessionId;
        this.chatId = options.chatId;

        this.workerChannel = new MessageChannel();
        this.worker = new Worker('/dist/opusEncoderWorker.js');
        this.worker.onmessage = this.onWorkerMessage;
        this.worker.onerror = this.onWorkerError;

        const loadEncoder: LoadEncoderMessage = {
            type: 'loadEncoder',
            mimeType: 'audio/webm',
            wasmPath: WebMOpusWasm as string,
        }
        this.postMessageToWorker(loadEncoder);
    }

    public setSource(source: MediaStreamAudioSourceNode): void {
        if (this.source === source) {
            return;
        }

        if (this.source) {
            this.source.disconnect();
        }

        this.source = source;
        this.stream = source.mediaStream;
    }

    public override dispatchEvent(event: Event): boolean {
        const { type } = event;
        switch (type) {
            case 'datarecorded':
                if (this.ondatarecorded) {
                    this.ondatarecorded(event as DataEvent);
                }
                break;
            case 'dataavailable':
                if (this.ondataavailable) {
                    this.ondataavailable(event as BlobEvent);
                }
                break;
            case 'error':
                if (this.onerror) {
                    this.onerror(event as MediaRecorderErrorEvent);
                }
                break;
            case 'pause':
                if (this.onpause) {
                    this.onpause(event);
                }
                break;
            case 'resume':
                if (this.onresume) {
                    this.onresume(event);
                }
                break;
            case 'start':
                if (this.onstart) {
                    this.onstart(event);
                }
                break;
            case 'stop':
                if (this.onstop) {
                    this.onstop(event);
                }
                break;
        }
        return super.dispatchEvent(event);
    }

    public pause(): void {
        if (this.state === 'inactive') {
            throw new Error('DOMException: INVALID_STATE_ERR, state must NOT be inactive.');
        }

        // Stop stream first
        this.source.disconnect();
        this.encoderWorklet.disconnect();

        const event = new Event('pause');
        this.dispatchEvent(event);
        this.state = 'paused';
    }

    public requestData(): void {
        if (this.state === 'inactive') {
            throw new Error('DOMException: INVALID_STATE_ERR, state must NOT be inactive.');
        }

        throw new Error('method is unsupported');
    }

    public resume(): void {
        if (this.state === 'inactive') {
            throw new Error('DOMException: INVALID_STATE_ERR, state must NOT be inactive.');
        }

        // Restart streaming data
        this.source.connect(this.encoderWorklet);

        const event = new Event('resume');
        this.dispatchEvent(event);
        this.state = 'recording';
    }

    public start(timeSlice?: number): void {
        if (this.state !== 'inactive') {
            throw new Error('DOMException: INVALID_STATE_ERR, state must be inactive.');
        }
        if (timeSlice < 0) {
            throw new TypeError('invalid arguments, timeSlice should be 0 or higher.');
        }
        if (this.source == null) {
            throw new Error('start: streamNode is not set');
        }

        const tracks = this.stream.getAudioTracks();
        if (!tracks[0]) {
            throw new Error('DOMException: UnknownError, media track not found.');
        }

        void this.initialize(timeSlice);

        this.state = 'recording';

        // If the worker is already loaded then start
        if (this.workerState === 'readyToInit') {
            const { channelCount, audioBitsPerSecond, sessionId, chatId } = this;
            const initMessage: InitMessage = {
                type: 'init',
                sampleRate: SAMPLE_RATE,
                channelCount: channelCount,
                bitsPerSecond: audioBitsPerSecond,
                sessionId: sessionId,
                chatId: chatId,
            };
            this.postMessageToWorker(initMessage);
        }
    }

    public async startAsync(source: MediaStreamAudioSourceNode, timeSlice: number): Promise<void> {
        if (this.source === source)
            return;

        if (this.source)
            this.source.disconnect();

        this.source = source;
        this.stream = source.mediaStream;

        const tracks = this.stream.getAudioTracks();
        if (!tracks[0]) {
            throw new Error('DOMException: UnknownError, media track not found.');
        }

        // Start recording
        await this.initialize(timeSlice);

        this.state = 'recording';

        // If the worker is already loaded then start
        if (this.workerState === 'readyToInit') {
            const { channelCount, audioBitsPerSecond, sessionId, chatId } = this;
            const initMessage: InitMessage = {
                type: 'init',
                sampleRate: SAMPLE_RATE,
                channelCount: channelCount,
                bitsPerSecond: audioBitsPerSecond,
                sessionId: sessionId,
                chatId: chatId,
            };
            this.postMessageToWorker(initMessage);
        }
    }

    public stop(): void {
        if (this.state === 'inactive') {
            throw new Error('DOMException: INVALID_STATE_ERR, state must NOT be inactive.');
        }

        // Stop stream first
        this.source.disconnect();
        this.encoderWorklet.disconnect();

        // Stop event will be triggered at _onmessageFromWorker(),
        const doneMessage: DoneMessage = {
            type: 'done'
        }
        this.postMessageToWorker(doneMessage);

        this.state = 'inactive';
    }

    public stopAsync(): Promise<void> {
        return new Promise(resolve => {
            this.stopResolve = resolve;
            this.stop();
        });
    }

    private async initialize(timeSlice: number): Promise<void> {
        if (this.context == null) {
            this.context = await AudioContextPool.get('main') as AudioContext;
            if (this.context.sampleRate != 48000) {
                throw new Error(`initialize: AudioContext sampleRate should be 48000, but sampleRate=${ this.context.sampleRate }`);
            }
            const encoderWorkletOptions: AudioWorkletNodeOptions = {
                numberOfInputs: 1,
                numberOfOutputs: 1,
                channelCount: 1,
                channelInterpretation: 'speakers',
                channelCountMode: 'explicit',
                processorOptions: {
                    timeSlice: timeSlice
                } as ProcessorOptions,
            };
            this.encoderWorklet = new AudioWorkletNode(this.context, 'opus-encoder-worklet-processor', encoderWorkletOptions);
            const initPortMessage: EncoderWorkletMessage = {
                type: 'init-port',
            }
            this.encoderWorklet.port.postMessage(initPortMessage, [this.workerChannel.port2]);
        }
    }

    private postMessageToWorker (encoderMessage: EncoderMessage) {
        const { type } = encoderMessage;
        switch (type) {
            case 'loadEncoder':
                // Expected 'readyToInit' event from the worker.
                this.worker.postMessage(encoderMessage, [this.workerChannel.port1]);
                break;

            case 'init':
                // Initialize the worker
                // Expected 'initCompleted' event from the worker.
                this.worker.postMessage(encoderMessage);
                break;

            case 'done':
                // Tell encoder finalize the job and destroy itself.
                // Expected 'doneCompleted' event from the worker.
                this.worker.postMessage(encoderMessage);
                break;

            default:
                // This is an error case
                throw new Error('Internal Error: Incorrect postMessage requested.');
        }
    }

    private onWorkerMessage = (ev: MessageEvent<EncoderResponseMessage>) => {
        const { type } = ev.data;
        switch (type) {
            case 'readyToInit':
                this.workerState = 'readyToInit';
                if (this.state === 'recording') {
                    const { channelCount, audioBitsPerSecond, sessionId, chatId } = this;
                    const initMessage: InitMessage = {
                        type: 'init',
                        sampleRate: SAMPLE_RATE,
                        channelCount: channelCount,
                        bitsPerSecond: audioBitsPerSecond,
                        sessionId: sessionId,
                        chatId: chatId,
                    };
                    this.postMessageToWorker(initMessage);
                }
                break;

            case 'initCompleted':
                // Start streaming
                this.source.connect(this.encoderWorklet);
                this.workerState = 'encoding';
                this.dispatchEvent( new Event('start'));
                break;

            case 'doneCompleted':
                // Detect of stop() called before
                this.dispatchEvent(new Event('stop'));

                this.workerState = 'readyToInit';
                if (this.stopResolve) {
                    const resolve = this.stopResolve;
                    this.stopResolve = null;
                    resolve();
                }
                break;

            default:
                break; // Ignore
        }
    }

    private onWorkerError = (error: ErrorEvent) => {
        // Stop stream first
        this.source.disconnect();
        this.encoderWorklet.disconnect();
        this.workerState = 'readyToInit'

        // Send message to host
        const message = [
            `FileName: ${error.filename}`,
            `LineNumber: ${error.lineno}`,
            `Message: ${error.message}`
        ].join(' - ');
        const errorToPush = new Event('error');
        errorToPush['name'] = 'UnknownError';
        errorToPush['message'] = message;
        this.dispatchEvent(errorToPush);
    }
}
