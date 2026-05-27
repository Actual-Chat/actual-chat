// Output-side switch between encode and wireSend. When closed, EncodedBundles
// are disposed in place — encoder keeps running (so the codec/HW slot stays
// warm and runtime stats keep updating) but nothing reaches the wire. Used
// during JoinVideoCallModal warmup: pipeline runs locally, server sees nothing
// until the user clicks Join (which flips the gate open).

import { from, type PipeOperator } from 'ix-ext';
import { disposeEncodedBundle, type EncodedBundle } from '../frame-envelopes';

export interface WireGateState {
    isOpen(): boolean;
}

export class MutableWireGate implements WireGateState {
    private _isOpen: boolean;

    constructor(initialOpen: boolean) {
        this._isOpen = initialOpen;
    }

    public isOpen(): boolean {
        return this._isOpen;
    }

    public setOpen(open: boolean): void {
        this._isOpen = open;
    }
}

export function wireGate(state: WireGateState): PipeOperator<EncodedBundle, EncodedBundle> {
    return source => {
        return from(impl());

        async function* impl(): AsyncIterable<EncodedBundle> {
            for await (const bundle of source) {
                if (state.isOpen()) {
                    yield bundle;
                    continue;
                }
                disposeEncodedBundle(bundle);
            }
        }
    };
}
