import WebMOpusWasm from 'opus-media-recorder/WebMOpusEncoder.wasm';
import { AudioContextPool } from 'audio-context-pool';
import { ResolveCallbackMessage } from 'resolve-callback-message';

import { ProcessorOptions } from './worklets/opus-encoder-worklet-processor';
import { EncoderWorkletMessage } from './worklets/opus-encoder-worklet-message';
import { DoneMessage, InitNewStreamMessage, LoadModuleMessage } from './workers/opus-encoder-worker-message';
import { VadMessage } from './workers/audio-vad-worker-message';
import { VadWorkletMessage } from './worklets/audio-vad-worklet-message';

type WorkerState = 'inactive'|'readyToInit'|'encoding';

const mimeType = 'audio/webm';
export class OpusMediaRecorder extends EventTarget {
    private readonly worker: Worker;
    private readonly vadWorker: Worker;
    private readonly channelCount: number = 1;
    private readonly encoderWorkerChannel: MessageChannel;
    private readonly vadWorkerChannel: MessageChannel;

    private context: AudioContext = null;
    private workerState: WorkerState = 'inactive';
    private encoderWorklet: AudioWorkletNode = null;
    private vadWorklet: AudioWorkletNode = null;
    private loadCompleted: Promise<void>;
    private lastCallbackId: number = 0;
    private callbacks = new Map<number, Function>();

    public source?: MediaStreamAudioSourceNode = null;
    public stream: MediaStream;
    public readonly audioBitsPerSecond: number;
    public readonly mimeType: string = mimeType;

    public state: RecordingState = 'inactive';
    public onerror: ((ev: MediaRecorderErrorEvent) => void) | null;

    constructor(options: MediaRecorderOptions) {
        super();

        this.audioBitsPerSecond = options.audioBitsPerSecond;

        this.encoderWorkerChannel = new MessageChannel();
        this.worker = new Worker('/dist/opusEncoderWorker.js');
        this.worker.onmessage = this.onWorkerMessage;
        this.worker.onerror = this.onWorkerError;

        this.vadWorkerChannel = new MessageChannel();
        this.vadWorker = new Worker('/dist/vadWorker.js');

        this.loadCompleted = this.loadWorkers();
    }

    public override dispatchEvent(event: Event): boolean {
        const { type } = event;
        switch (type) {
            case 'error':
                if (this.onerror) {
                    this.onerror(event as MediaRecorderErrorEvent);
                }
                break;
            default:
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
        this.vadWorklet.disconnect();

        const event = new Event('pause');
        this.dispatchEvent(event);
        this.state = 'paused';
    }

    public resume(): void {
        if (this.state === 'inactive') {
            throw new Error('DOMException: INVALID_STATE_ERR, state must NOT be inactive.');
        }

        // Restart streaming data
        this.source.connect(this.encoderWorklet);
        this.source.connect(this.vadWorklet);

        const event = new Event('resume');
        this.dispatchEvent(event);
        this.state = 'recording';
    }

    public async startAsync(source: MediaStreamAudioSourceNode, timeSlice: number, sessionId: string, chatId: string, debugMode = false): Promise<void> {
        if (this.source === source)
            return;

        if (sessionId == '' || chatId == '')
            throw new Error('OpusMediaRecorder.startAsync: sessionId and chatId both should have value specified.');

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

        if (this.loadCompleted != null) {
            await this.loadCompleted;

            this.loadCompleted = null;
            this.workerState = 'readyToInit';
        }

        const callbackId = this.lastCallbackId++;
        await new Promise<void>(resolve => {
            this.callbacks.set(callbackId, resolve);

            const { channelCount, audioBitsPerSecond } = this;
            const initMessage: InitNewStreamMessage = {
                type: 'init',
                channelCount: channelCount,
                bitsPerSecond: audioBitsPerSecond,
                sessionId: sessionId,
                chatId: chatId,
                debugMode: debugMode,
                callbackId
            };
            // Initialize the worker
            // Expected 'initCompleted' event from the worker.
            this.worker.postMessage(initMessage);

            // Initialize new stream at the VAD worker
            const vadInitMessage: VadMessage = {
                type: 'init',
            }
            this.vadWorker.postMessage(vadInitMessage);
        });

        // Start streaming
        this.source.connect(this.encoderWorklet);
        // It's OK to not wait for VAD worker init-new-stream message to be processed
        this.source.connect(this.vadWorklet);
        this.workerState = 'encoding';
        this.dispatchEvent( new Event('start'));
    }

    public async stopAsync(): Promise<void> {
        const callbackId = this.lastCallbackId++;
        await new Promise(resolve => {
            this.callbacks.set(callbackId, resolve);
            if (this.state === 'inactive') {
                throw new Error('DOMException: INVALID_STATE_ERR, state must NOT be inactive.');
            }

            // Stop stream first
            this.source.disconnect();
            this.encoderWorklet.disconnect();
            this.vadWorklet.disconnect();

            // Stop event will be triggered at _onmessageFromWorker(),
            const doneMessage: DoneMessage = {
                type: 'done',
                callbackId
            }
            // Tell encoder finalize the job and destroy itself.
            // Expected 'doneCompleted' event from the worker.
            this.worker.postMessage(doneMessage);

            this.state = 'inactive';
        });

        // Detect of stop() called before
        this.dispatchEvent(new Event('stop'));
        this.workerState = 'readyToInit';
    }

    private loadWorkers(): Promise<void> {
        // Setting the url to the href property of an anchor tag handles normalization
        // for us. There are 3 main cases.
        // 1. Relative path normalization e.g "b" -> "http://localhost:5000/a/b"
        // 2. Absolute path normalization e.g "/a/b" -> "http://localhost:5000/a/b"
        // 3. Network path reference normalization e.g "//localhost:5000/a/b" -> "http://localhost:5000/a/b"
        const base = new URL('opus-media-recorder.ts', import.meta.url).origin;
        const audioHubUrl = new URL('/api/hub/audio', base).toString();
        const wasmPathUrl = new URL(WebMOpusWasm, base).toString();

        const callbackId = this.lastCallbackId++;
        return new Promise(resolve => {
            this.callbacks.set(callbackId, resolve);

            const loadEncoder: LoadModuleMessage = {
                type: 'load',
                mimeType: 'audio/webm',
                wasmPath: wasmPathUrl,
                audioHubUrl: audioHubUrl,
                callbackId: callbackId,
            }

            const crossWorkerChannel = new MessageChannel();
            // Expected 'loadCompleted' event from the worker.
            this.worker.postMessage(loadEncoder, [this.encoderWorkerChannel.port1, crossWorkerChannel.port1]);

            const loadVad: VadMessage = {
                type: 'load',
            }
            this.vadWorker.postMessage(loadVad, [this.vadWorkerChannel.port1, crossWorkerChannel.port2]);
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
                type: 'init',
            }
            this.encoderWorklet.port.postMessage(initPortMessage, [this.encoderWorkerChannel.port2]);

            const vadWorkletOptions: AudioWorkletNodeOptions = {
                numberOfInputs: 1,
                numberOfOutputs: 1,
                channelCount: 1,
                channelInterpretation: 'speakers',
                channelCountMode: 'explicit',
            };
            this.vadWorklet = new AudioWorkletNode(this.context, 'audio-vad-worklet-processor', vadWorkletOptions);
            const vadInitPortMessage: VadWorkletMessage = {
                type: 'init',
            }
            this.vadWorklet.port.postMessage(vadInitPortMessage, [this.vadWorkerChannel.port2]);
        }
    }

    private onWorkerMessage = (ev: MessageEvent<ResolveCallbackMessage>) => {
        const { callbackId } = ev.data;
        this.popCallback(callbackId)();
    };

    private popCallback(callbackId: number): Function {
        const callback = this.callbacks.get(callbackId);
        if (callback === undefined) {
            throw new Error(`OpusMediaRecorder: Callback with id '${callbackId}' is not found.`);
        }
        this.callbacks.delete(callbackId);
        return callback;
    }

    private onWorkerError = (error: ErrorEvent) => {
        // Stop stream first
        if (this.source)
            this.source.disconnect();
        if (this.encoderWorklet)
            this.encoderWorklet.disconnect();
        if (this.vadWorklet)
            this.vadWorklet.disconnect();

        this.workerState = 'readyToInit';

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
    };
}
