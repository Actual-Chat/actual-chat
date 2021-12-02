interface AudioWorkletProcessor {
    readonly port: MessagePort;
    process(
        inputs: Float32Array[][],
        outputs: Float32Array[][],
        parameters: Record<string, Float32Array>
    ): boolean;
}

interface AudioParamDescriptor {
    defaultValue?: number;
    maxValue?: number;
    minValue?: number;
    name: string;
}

interface IAudioWorkletProcessor {
    process(inputs: Float32Array[][], outputs: Float32Array[][], parameters: { [name: string]: Float32Array }): boolean;
}

declare var AudioWorkletProcessor: {
    prototype: AudioWorkletProcessor;
    new (options?: AudioWorkletNodeOptions): AudioWorkletProcessor;
};
  
declare function registerProcessor(
        name: string,
        processorCtor: (new (
        options?: AudioWorkletNodeOptions
    ) => AudioWorkletProcessor) & {
        parameterDescriptors?: AudioParamDescriptor[];
    }): undefined;
