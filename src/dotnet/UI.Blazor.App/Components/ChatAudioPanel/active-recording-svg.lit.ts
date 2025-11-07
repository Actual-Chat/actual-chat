import { customElement, property, state } from 'lit/decorators.js';
import { css, html, LitElement } from 'lit';
import { filter, scan, Subscription } from 'rxjs';
import { clamp, RunningMax, translate } from 'math';
import { RecorderStateHub } from '../AudioRecorder/recorder-state-hub';

const SIGNAL_COUNT_TO_CALCULATE_MAX = 100;

interface Result {
    runningMax: RunningMax;
    p: number;
    i: number;
}

interface AudioPowerState {
    height1: number;
    height2: number;
    height3: number;
}

const MIN_HEIGHT = 10;
const MAX_HEIGHT = 100;
const DEFAULT_STATE: AudioPowerState = {
    height1: MIN_HEIGHT,
    height2: MIN_HEIGHT,
    height3: MIN_HEIGHT,
};

@customElement('active-recording-svg')
class ActiveRecordingSvg extends LitElement {
    static styles = [css`
        :host {
            display: flex;
        }
        div {
            display: flex;
            justify-content: center;
            align-self: center;
        }
        rect {
            transition-duration: 0s;
            will-change: transform;
        }
        rect.non-active {
            opacity: 0;
            transition-duration: 0.25s;
            transition-property: opacity;
            @starting-style {
                opacity: 1;
            }
        }
        rect.active {
            opacity: 1;
            transition-duration: 0.25s;
            transition-property: opacity;
            @starting-style {
                opacity: 0;
            }
        }
        rect#record-rect-2.in-rest {
            animation: wave 1.3s steps(10, start) infinite;
        }
        rect#record-rect-3.in-rest {
            animation: wave 1.3s steps(10, start) infinite -1.1s;
        }
        rect#record-rect-4.in-rest {
            animation: wave 1.3s steps(10, start) infinite -0.9s;
        }

        @keyframes wave {
            0%, 60%, 100% {
                transform: initial;
            }
            30% {
                transform: translateY(-5px);
            }
        }
    `];

    private _isRecording: boolean = false;
    private observer: IntersectionObserver;
    private recorderStateChangedSubscription: Subscription;
    private signalPowerChangedSubscription: Subscription;

    @state({
        hasChanged: (value: AudioPowerState, oldValue: AudioPowerState | null): boolean =>
            value.height1 !== oldValue?.height1 || value.height2 !== oldValue?.height2 || value.height3 !== oldValue?.height3
    })
    private audioPowerState: AudioPowerState = DEFAULT_STATE;
    @state()
    private isVoiceActive = false;
    @state()
    private isVisible = false;

    @property({type: Number})
    size = 10;

    @property({type: Boolean})
    set isRecording(val: boolean) {
        if (val)
            this.audioPowerState = DEFAULT_STATE;
        this._isRecording = val;
    }

    get isRecording() { return this._isRecording };

    connectedCallback() {
        super.connectedCallback();

        this.observer = new IntersectionObserver(entries => {
            this.isVisible = entries.some(e => e.isIntersecting);
            // console.warn('isVisible', isVisible, entries);
        });
        this.observer.observe(this);

        const recorderState$ = RecorderStateHub.recorderStateChanged$;
        const signalPower$ = RecorderStateHub.audioPowerChanged$
            .pipe(filter((p, i) => i % 2 === 0))
            .pipe(scan<number, Result, RunningMax>((runningMaxOrResult, p, i) => {
                const runningMax: RunningMax = runningMaxOrResult['runningMax'] || runningMaxOrResult;
                // Fill the window first; once full, only advance it while voice is active
                const isWindowNotFull = runningMax.sampleCount < SIGNAL_COUNT_TO_CALCULATE_MAX;
                const shouldAppend = isWindowNotFull || this.isVoiceActive;
                if (shouldAppend)
                    runningMax.appendSample(p);
                return { runningMax, p, i };
            }, new RunningMax(SIGNAL_COUNT_TO_CALCULATE_MAX, 0)));

        this.recorderStateChangedSubscription = recorderState$.subscribe(rs => {
            this.isVoiceActive = rs.isVoiceActive;
            this._isRecording = rs.isRecording;
        });
        const getHeight = (power: number, maxPower: number) => Math.floor((translate(power, [0, 0.6*maxPower], [MIN_HEIGHT, MAX_HEIGHT]))) * 100 / MAX_HEIGHT;
        this.signalPowerChangedSubscription = signalPower$.subscribe(({ runningMax, p }) => {
            if (!this._isRecording || !this.isVoiceActive)
                return;

            const maxPower = runningMax.value;
            const maxSampleCount = runningMax.sampleCount;
            const prevAudioPower = runningMax.samples;
            const maxNotYetCalculatedAdjustment = maxSampleCount < SIGNAL_COUNT_TO_CALCULATE_MAX / 2
                ? clamp(maxSampleCount / (SIGNAL_COUNT_TO_CALCULATE_MAX / 4), 0, 0.8)
                : 0.8;
            const power1 = p * maxNotYetCalculatedAdjustment;
            const targetHeight1 = getHeight(power1, maxPower);
            const power2 = prevAudioPower[prevAudioPower.length - 4] * maxNotYetCalculatedAdjustment;
            const targetHeight2 = Math.floor(clamp(0.6 * getHeight(power2, maxPower), MIN_HEIGHT, MAX_HEIGHT));
            const power3 = prevAudioPower[prevAudioPower.length - 7] * maxNotYetCalculatedAdjustment;
            const targetHeight3 = Math.floor(clamp(0.4 * getHeight(power3, maxPower), MIN_HEIGHT, MAX_HEIGHT));
            const smoothHeight = (current: number, target: number) =>
                current + (target - current) * 0.6;
            const MAX_HEIGHT_WITH_PADDING = MAX_HEIGHT - 4;
            this.audioPowerState = {
                height1: Math.min(smoothHeight(this.audioPowerState.height1, targetHeight1), MAX_HEIGHT_WITH_PADDING),
                height2: Math.min(smoothHeight(this.audioPowerState.height2, targetHeight2), MAX_HEIGHT_WITH_PADDING),
                height3: Math.min(smoothHeight(this.audioPowerState.height3, targetHeight3), MAX_HEIGHT_WITH_PADDING),
            };
        });
    }

    disconnectedCallback() {
        super.disconnectedCallback();

        this.recorderStateChangedSubscription.unsubscribe();
        this.signalPowerChangedSubscription.unsubscribe();
    }

    protected render(): unknown {
        const { size, audioPowerState, isVoiceActive, isRecording, isVisible } = this;
        const width = 10;
        if (!this._isRecording) {
            const offset = 50 - MIN_HEIGHT / 2;
            return html`
                <svg xmlns='http://www.w3.org/2000/svg' width='${size * 4}' height='${size * 4}'
                     preserveAspectRatio='none'
                     viewBox='0 0 24 24' fill='none' stroke='var(--white)'
                     stroke-width='${width}%' stroke-linecap='round' stroke-linejoin='bevel'>
                    <rect id='record-rect-2' class='in-rest'
                          x='${width * 2.5}%'
                          y='${offset}%'
                          width='${width}%'
                          height='${MIN_HEIGHT}%'
                          fill='var(--white)' stroke-width='0' rx='5%' ry='5%'>
                    </rect>
                    <rect id='record-rect-3' class='in-rest'
                          x='${width * 4.5}%'
                          y='${offset}%'
                          width='${width}%'
                          height='${MIN_HEIGHT}%'
                          fill='var(--white)' stroke-width='0' rx='5%' ry='5%'>
                    </rect>
                    <rect id='record-rect-4' class='in-rest'
                          x='${width * 6.5}%'
                          y='${offset}%'
                          width='${width}%'
                          height='${MIN_HEIGHT}%'
                          fill='var(--white)' stroke-width='0' rx='5%' ry='5%'>
                    </rect>
                </svg>
            `;
        } else {
            const display = getComputedStyle(this.shadowRoot?.host!, null)?.display ?? 'none';
            if (display === 'none')
                return html``;

            const shouldAnimate = isVoiceActive && isVisible;
            const height1 = shouldAnimate ? audioPowerState.height1 : MIN_HEIGHT;
            const height2 = shouldAnimate ? audioPowerState.height2 : MIN_HEIGHT;
            const height3 = shouldAnimate ? audioPowerState.height3 : MIN_HEIGHT;
            const offset1 = 50 - height1 / 2;
            const offset2 = 50 - height2 / 2;
            const offset3 = 50 - height3 / 2;

            const edgeDotCls = shouldAnimate ? "active" : "non-active";
            const centerDotCls = isVoiceActive || !isVisible ? "" : "in-rest";

            return html`
                <svg xmlns='http://www.w3.org/2000/svg' width='${size * 4}' height='${size * 4}'
                     preserveAspectRatio='none'
                     viewBox='0 0 24 24' fill='none' stroke='var(--white)'
                     stroke-width='${width}%' stroke-linecap='round' stroke-linejoin='bevel'>
                    <rect id='record-rect-1' class='${edgeDotCls}'
                          x='${width / 2}%'
                          y='${offset3}%'
                          width='${width}%'
                          height='${height3}%'
                          fill='var(--white)' stroke-width='0' rx='5%' ry='5%'>
                    </rect>
                    <rect id='record-rect-2' class='${centerDotCls}'
                          x='${width * 2.5}%'
                          y='${offset2}%'
                          width='${width}%'
                          height='${height2}%'
                          fill='var(--white)' stroke-width='0' rx='5%' ry='5%'>
                    </rect>
                    <rect id='record-rect-3' class='${centerDotCls}'
                          x='${width * 4.5}%'
                          y='${offset1}%'
                          width='${width}%'
                          height='${height1}%'
                          fill='var(--white)' stroke-width='0' rx='5%' ry='5%'>
                    </rect>
                    <rect id='record-rect-4' class='${centerDotCls}'
                          x='${width * 6.5}%'
                          y='${offset2}%'
                          width='${width}%'
                          height='${height2}%'
                          fill='var(--white)' stroke-width='0' rx='5%' ry='5%'>
                    </rect>
                    <rect id='record-rect-5' class='${edgeDotCls}'
                          x='${width * 8.5}%'
                          y='${offset3}%'
                          width='${width}%'
                          height='${height3}%'
                          fill='var(--white)' stroke-width='0' rx='5%' ry='5%'>
                    </rect>
                </svg>
            `;
        }
    }
}
