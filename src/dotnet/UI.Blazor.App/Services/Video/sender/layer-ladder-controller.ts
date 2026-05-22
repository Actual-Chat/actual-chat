import type { EncoderConfigPerLayer } from '../operators/encode';

// Shared mutable handle the layer-aware operators read on every bundle.
// `version` increments on each `setConfigs`. Whole-state swap on mutation
// keeps reads atomic without locks (operators capture `current` per
// bundle entry, so a concurrent `setConfigs` either lands before the read
// or shows up as the next bundle's version bump).
export interface LadderState {
    readonly configs: readonly EncoderConfigPerLayer[];
    readonly version: number;
}

export class LayerLadderController {
    private state: LadderState;

    constructor(initial: readonly EncoderConfigPerLayer[]) {
        if (initial.length === 0)
            throw new Error('LayerLadderController: initial configs must not be empty');
        this.state = { configs: initial.slice(), version: 0 };
    }

    get current(): LadderState {
        return this.state;
    }

    setConfigs(next: readonly EncoderConfigPerLayer[]): number {
        if (next.length === 0)
            throw new Error('LayerLadderController: configs must not be empty');
        this.state = { configs: next.slice(), version: this.state.version + 1 };
        return this.state.version;
    }
}
