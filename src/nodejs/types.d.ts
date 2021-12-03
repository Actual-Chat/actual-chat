declare module "*.wasm" {
    const value: any;
    export = value;
}

declare module "*.onnx" {
    const value: any;
    export = value;
}

declare var AudioWorkletProcessor: {
    prototype: AudioWorkletProcessor;
    new(): AudioWorkletProcessor;
    // https://developer.mozilla.org/en-US/docs/Web/API/AudioWorkletProcessor/AudioWorkletProcessor
    new(options?: AudioWorkletNodeOptions): AudioWorkletProcessor;
};