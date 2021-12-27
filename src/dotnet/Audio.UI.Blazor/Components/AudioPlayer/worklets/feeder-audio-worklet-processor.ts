/** Part of the feeder that lives in AudioWorkletGlobalScope */
class FeederAudioWorkletProcessor extends AudioWorkletProcessor {
    constructor() {
        super();
    }
}

/** Part of the feeder that lives in main global scope. It's the counterpart of FeederAudioWorkletProcessor */
class FeederAudioWorkletNode extends AudioWorkletNode {
    constructor(context: BaseAudioContext, name: string, options?: AudioWorkletNodeOptions) {
        super(context, name, options);
    }
    // TODO: implement this :)
}