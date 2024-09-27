import { customElement, property, state } from 'lit/decorators.js';
import { css, html, LitElement } from 'lit';
import { filter, scan, Subscription } from 'rxjs';
import { OpusMediaRecorder } from '../AudioRecorder/opus-media-recorder';
import { clamp, RunningMax, translate } from 'math';

const SIGNAL_COUNT_TO_CALCULATE_MAX = 100; // 200 * 30ms = 3s

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
}

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

    private _isRecording = null;

    @state({
        hasChanged: (value: AudioPowerState, oldValue: AudioPowerState | null): boolean =>
            value.height1 !== oldValue?.height1 || value.height2 !== oldValue?.height2 || value.height3 !== oldValue?.height3
    })
    private audioPowerState: AudioPowerState = DEFAULT_STATE;
    @state()
    private isVoiceActive = false;

    @property({type: Number})
    size = 10;

    @property({type: Boolean})
    set isRecording(val: boolean) {
        if (val)
            this.audioPowerState = DEFAULT_STATE;
        this._isRecording = val;
    }

    get isRecording() { return this._isRecording };

    private readonly recorderStateChangedSubscription: Subscription;
    private readonly signalPowerChangedSubscription: Subscription;

    constructor() {
        super();

        const recorderState$ = OpusMediaRecorder.recorderStateChanged$;
        const signalPower$ = OpusMediaRecorder.audioPowerChanged$
            .pipe(filter((p, i) => i % 2 === 0))
            .pipe(scan<number, Result, RunningMax>((runningMaxOrResult, p, i) => {
                const runningMax: RunningMax = runningMaxOrResult['runningMax'] || runningMaxOrResult;
                runningMax.appendSample(p);
                return { runningMax, p, i };
            }, new RunningMax(SIGNAL_COUNT_TO_CALCULATE_MAX, 0)));

        this.recorderStateChangedSubscription = recorderState$.subscribe(rs => {
            this.isVoiceActive = rs.isVoiceActive;
            this._isRecording = rs.isRecording;
        });
        const getHeight = (power: number, maxPower: number) => Math.floor((translate(power, [0, 0.8*maxPower], [MIN_HEIGHT, MAX_HEIGHT]))) * 100 / MAX_HEIGHT;
        this.signalPowerChangedSubscription = signalPower$.subscribe(({ runningMax, p }) => {
            if (!this._isRecording || !this.isVoiceActive)
                return;

            if (this.audioPowerState === DEFAULT_STATE)
                runningMax.reset(); // On turn on recording

            const maxPower = runningMax.value;
            const maxSampleCount = runningMax.sampleCount;
            const prevAudioPower = runningMax.samples;
            const maxNotYetCalculatedAdjustment = maxSampleCount < SIGNAL_COUNT_TO_CALCULATE_MAX / 2
                ? clamp(maxSampleCount / (SIGNAL_COUNT_TO_CALCULATE_MAX / 4), 0, 1)
                : 1;
            const power1 = p * maxNotYetCalculatedAdjustment;
            const height1 = getHeight(power1, maxPower);
            const power2 = prevAudioPower[prevAudioPower.length - 4] * maxNotYetCalculatedAdjustment ?? 0; // with 180 ms delay
            const height2 = Math.floor(clamp(0.7 * getHeight(power2, maxPower), MIN_HEIGHT, MAX_HEIGHT));
            const power3 = prevAudioPower[prevAudioPower.length - 7] * maxNotYetCalculatedAdjustment ?? 0; // with 360 ms delay
            const height3 = Math.floor(clamp(0.4 * getHeight(power3, maxPower), MIN_HEIGHT, MAX_HEIGHT));
            this.audioPowerState = {
                height1: height1,
                height2: height2,
                height3: height3,
            }
        });
    }

    disconnectedCallback() {
        super.disconnectedCallback();

        this.recorderStateChangedSubscription.unsubscribe();
        this.signalPowerChangedSubscription.unsubscribe();
    }

    protected render(): unknown {
        const { size, audioPowerState, isVoiceActive, isRecording } = this;
        if (isRecording === false)
            return html``;

        const display = getComputedStyle(this.shadowRoot?.host, null)?.display ?? 'none';
        if (display === 'none')
            return html``;

        const width = 10;
        const height1 = isVoiceActive ? audioPowerState.height1 : MIN_HEIGHT;
        const height2 = isVoiceActive ? audioPowerState.height2 : MIN_HEIGHT;
        const height3 = isVoiceActive ? audioPowerState.height3 : MIN_HEIGHT;
        const offset1 = 50 - height1 / 2;
        const offset2 = 50 - height2 / 2;
        const offset3 = 50 - height3 / 2;

        const edgeDotCls = isVoiceActive ? "active" : "non-active";
        const centerDotCls = isVoiceActive ? "" : "in-rest";

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
