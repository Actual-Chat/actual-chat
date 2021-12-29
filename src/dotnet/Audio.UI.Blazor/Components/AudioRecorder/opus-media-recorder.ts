import WebMOpusWasm from 'opus-media-recorder/WebMOpusEncoder.wasm';
import {AudioContextPool} from 'audio-context-pool';

import {
    DoneCommand,
    EncoderCommand,
    EncoderMessage,
    GetEncodedDataCommand,
    InitCommand,
    LoadEncoderCommand,
} from "./opus-media-recorder-message";

type WorkerState = 'inactive'|'readyToInit'|'encoding'|'closed';

const mimeType: string = 'audio/webm';
export class OpusMediaRecorder extends EventTarget implements MediaRecorder {
    private readonly worker: Worker;
    private readonly channelCount: number = 1;
    private readonly workerChannel: MessageChannel;

    private sampleRate: number = 48000;
    private context: AudioContext = null;
    private workerState: WorkerState = 'inactive';
    private source: MediaStreamAudioSourceNode = null;
    private encoderWorklet: AudioWorkletNode = null;
    private stopResolve: () => void = null;

    public readonly stream: MediaStream;
    public readonly videoBitsPerSecond: number = NaN;
    public readonly audioBitsPerSecond: number;
    public readonly mimeType: string = mimeType;

    public state: RecordingState = 'inactive';

    public ondataavailable: ((ev: BlobEvent) => any) | null;
    public onerror: ((ev: MediaRecorderErrorEvent) => any) | null;
    public onpause: ((ev: Event) => any) | null;
    public onresume: ((ev: Event) => any) | null;
    public onstart: ((ev: Event) => any) | null;
    public onstop: ((ev: Event) => any) | null;

    constructor(stream: MediaStream, options: MediaRecorderOptions) {
        super();

        this.stream = stream;
        this.audioBitsPerSecond = options.audioBitsPerSecond;

        this.workerChannel = new MessageChannel();
        this.worker = new Worker('/dist/opusEncoderWorker.js');
        this.worker.onmessage = (e) => this.onWorkerMessage(e);
        this.worker.onerror = (e) => this.onWorkerError(e);

        this.postMessageToWorker(new LoadEncoderCommand(WebMOpusWasm));
    }


    public override dispatchEvent(event: Event): boolean {
        const { type } = event;
        switch (type) {
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

        let event = new Event('pause');
        this.dispatchEvent(event);
        this.state = 'paused';
    }

    public requestData(): void {
        if (this.state === 'inactive') {
            throw new Error('DOMException: INVALID_STATE_ERR, state must NOT be inactive.');
        }

        // dataavailable event will be triggerd at onmessageFromWorker()
        this.postMessageToWorker(new GetEncodedDataCommand());
    }

    public resume(): void {
        if (this.state === 'inactive') {
            throw new Error('DOMException: INVALID_STATE_ERR, state must NOT be inactive.');
        }

        // Restart streaming data
        this.source.connect(this.encoderWorklet);

        let event = new Event('resume');
        this.dispatchEvent(event);
        this.state = 'recording';
    }

    public start(timeslice?: number): void {
        if (this.state !== 'inactive') {
            throw new Error('DOMException: INVALID_STATE_ERR, state must be inactive.');
        }
        if (timeslice < 0) {
            throw new TypeError('invalid arguments, timeslice should be 0 or higher.');
        }

        // Check worker is closed (usually by stop()) and init.
        if (this.workerState === 'closed') {
            this.workerState = 'readyToInit';
        }

        // Get channel count and sampling rate
        // channelCount: https://www.w3.org/TR/mediacapture-streams/#media-track-settings
        // sampleRate: https://developer.mozilla.org/en-US/docs/Web/API/BaseAudioContext/sampleRate
        let tracks = this.stream.getAudioTracks();
        if (!tracks[0]) {
            throw new Error('DOMException: UnkownError, media track not found.');
        }

        // Start recording
        this.initialize(timeslice).then(() => {
            this.state = 'recording';
        })

        // If the worker is already loaded then start
        if (this.workerState === 'readyToInit') {
            const { sampleRate, channelCount, audioBitsPerSecond } = this;
            const initCommand = new InitCommand(sampleRate, channelCount, audioBitsPerSecond);
            this.postMessageToWorker(initCommand);
        }
    }

    public stop(): void {
        if (this.state === 'inactive') {
            throw new Error('DOMException: INVALID_STATE_ERR, state must NOT be inactive.');
        }

        // Stop stream first
        this.source.disconnect();
        this.encoderWorklet.disconnect();
        // this.context.close();
        // this.context.

        // Stop event will be triggered at _onmessageFromWorker(),
        this.postMessageToWorker(new DoneCommand());

        this.state = 'inactive';
    }

    public stopAsync(): Promise<void> {
        return new Promise(resolve => {
            this.stopResolve = resolve;
            this.stop();
        });
    }

    private async initialize(timeslice: number): Promise<void> {
        this.context = await AudioContextPool.get("main") as AudioContext;
        this.sampleRate = this.context.sampleRate;
        this.source = this.context.createMediaStreamSource(this.stream);
        const encoderWorkletOptions: AudioWorkletNodeOptions = {
            numberOfInputs: 1,
            numberOfOutputs: 1,
            channelCount: 1,
            channelInterpretation: 'speakers',
            channelCountMode: 'explicit',
            processorOptions: {
                timeslice: timeslice
            }
        };
        this.encoderWorklet = new AudioWorkletNode(this.context, 'opus-encoder-worklet-processor', encoderWorkletOptions);
        this.encoderWorklet.port.postMessage({ topic: 'init-port' }, [this.workerChannel.port2]);
    }

    private postMessageToWorker (encoderCommand: EncoderCommand) {
        const { command } = encoderCommand;
        switch (command) {
            case 'loadEncoder':
                this.worker.postMessage(encoderCommand, [this.workerChannel.port1]);
                break;

            case 'init':
                // Initialize the worker
                this.worker.postMessage(encoderCommand);
                this.workerState = 'encoding';

                // Start streaming
                this.source.connect(this.encoderWorklet);
                let eventToPush = new Event('start');
                this.dispatchEvent(eventToPush);
                break;

            case 'getEncodedData':
                // Request encoded result.
                // Expected 'encodedData' event from the worker
                this.worker.postMessage({ command });
                break;

            case 'done':
                // Tell encoder finallize the job and destory itself.
                // Expected 'lastEncodedData' event from the worker.
                this.worker.postMessage({ command });
                break;

            default:
                // This is an error case
                throw new Error('Internal Error: Incorrect postMessage requested.');
        }
    }

    private onWorkerMessage(ev: MessageEvent<EncoderMessage>) {
        const { command, buffers } = ev.data;
        let eventToPush;
        switch (command) {
            case 'readyToInit':
                const { sampleRate, channelCount, audioBitsPerSecond } = this;
                this.workerState = 'readyToInit';

                // If start() is already called initialize worker
                if (this.state === 'recording') {
                    const initCommand = new InitCommand(sampleRate, channelCount, audioBitsPerSecond);
                    this.postMessageToWorker(initCommand);
                }
                break;

            case 'encodedData':
            case 'lastEncodedData':
                let data = new Blob(buffers, {'type': mimeType});
                eventToPush = new Event('dataavailable');
                eventToPush.data = data;
                this.dispatchEvent(eventToPush);

                // Detect of stop() called before
                if (command === 'lastEncodedData') {
                    eventToPush = new Event('stop');
                    this.dispatchEvent(eventToPush);

                    this.workerState = 'closed';
                    if (this.stopResolve) {
                        const resolve = this.stopResolve;
                        this.stopResolve = null;
                        resolve();
                    }
                }
                break;

            default:
                break; // Ignore
        }
    }

    onWorkerError (error) {
        // Stop stream first
        this.source.disconnect();
        this.encoderWorklet.disconnect();

        this.workerState = 'readyToInit'

        // Send message to host
        let message = [
            'FileName: ' + error.filename,
            'LineNumber: ' + error.lineno,
            'Message: ' + error.message
        ].join(' - ');
        let errorToPush = new Event('error');
        errorToPush["name"] = 'UnknownError';
        errorToPush["message"] = message;
        this.dispatchEvent(errorToPush);
    }
}
