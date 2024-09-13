import { KaiserBesselDerivedWindow } from 'math';

export class AudioStreamFader {
    private readonly kbdWindow: Float32Array;
    private fade: 'in' | 'out' | 'none' = 'none';
    private fadeWindowIndex: number | null = null;

    constructor(private readonly fadeSamples: number) {
        this.kbdWindow = KaiserBesselDerivedWindow(fadeSamples, 2.55);
    }

    public fadeIn(): void {
        this.fade = 'in';
        this.fadeWindowIndex = 0;
    }

    public fadeOut(): void {
        this.fade = 'out';
        this.fadeWindowIndex = 0;
    }

    public process(samples: Float32Array): Float32Array {
        const { fade,  kbdWindow, fadeSamples } = this;
        let fadeWindowIndex = this.fadeWindowIndex;
        if (fadeWindowIndex !== null) {
            if (fade === 'in')
                for (let i = 0; i < samples.length && fadeWindowIndex < kbdWindow.length; i++)
                    samples[i] *= kbdWindow[fadeWindowIndex++];
            else if (fade === 'out')
                for (let i = 0; i < samples.length && fadeWindowIndex < kbdWindow.length; i++)
                    samples[i] *= kbdWindow[kbdWindow.length - 1 - fadeWindowIndex++];

            if (fadeWindowIndex >= fadeSamples) {
                this.fadeWindowIndex = null;
                this.fade = 'none';
            }
            else
                this.fadeWindowIndex = fadeWindowIndex;
        }
        return samples;
    }
}
