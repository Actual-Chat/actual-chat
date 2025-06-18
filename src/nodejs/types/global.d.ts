declare global {

    interface AudioWorkletProcessor {
        // https://developer.mozilla.org/en-US/docs/Web/API/AudioWorkletProcessor/process
        process(inputs: Float32Array[][], outputs: Float32Array[][], parameters: Record<string, Float32Array>): boolean;
        readonly port: MessagePort;
        // https://developer.mozilla.org/en-US/docs/Web/API/AudioWorkletProcessor/AudioWorkletProcessor
        // eslint-disable-next-line @typescript-eslint/no-misused-new
        new(options?: AudioWorkletNodeOptions): AudioWorkletProcessor;
    }
}

export { };
