declare global {

    interface AudioWorkletProcessor {
        // https://developer.mozilla.org/en-US/docs/Web/API/AudioWorkletProcessor/process
        process(inputs: Float32Array[][], outputs: Float32Array[][], parameters: { [name: string]: Float32Array; }): boolean;
        readonly port: MessagePort;
        // https://developer.mozilla.org/en-US/docs/Web/API/AudioWorkletProcessor/AudioWorkletProcessor
        new(options?: AudioWorkletNodeOptions): AudioWorkletProcessor;
    }
}

export { };
