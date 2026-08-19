import { customElement, property, state } from 'lit/decorators.js';
import { css, html, LitElement } from 'lit';
import { AnimationSync } from 'animation-sync';
import { fastWriteRaf10Async } from 'fast-raf';
import { scan, Subscription } from 'rxjs';
import { clamp, RunningMax, translate, RunningEMA } from 'math';
import { RecordingActivity } from '../AudioRecorder/recording-activity';

const SIGNAL_COUNT_TO_CALCULATE_MAX = 100; // 100 * 96ms ~ 10s

interface Result {
    runningMax: RunningMax;
    runningEMA: RunningEMA;
    p: number;
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
export class ActiveRecordingSvg extends LitElement {
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
        }
        rect.in-rest {
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
            animation: wave 1.3s steps(13, start) infinite;
        }
        rect#record-rect-3.in-rest {
            animation: wave 1.3s steps(13, start) infinite -1.1s;
        }
        rect#record-rect-4.in-rest {
            animation: wave 1.3s steps(13, start) infinite -0.9s;
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

    private recentPowers: number[] = [];
    private _isRecording = false;
    private observer: IntersectionObserver;
    private recorderStateChangedSubscription: Subscription;
    private signalPowerChangedSubscription: Subscription;

    @state({
        hasChanged: (value: AudioPowerState, oldValue: AudioPowerState | null): boolean =>
            // eslint-disable-next-line
            value.height1 !== oldValue?.height1 || value.height2 !== oldValue?.height2 || value.height3 !== oldValue?.height3
    })
    private audioPowerState: AudioPowerState = DEFAULT_STATE;
    @state()
    private isVoiceActive = false;
    @state()
    private isVisible = false;

    @property({ type: Number }) size = 10;

    @property({ type: Boolean })
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

        const initialResult: Result = {
            runningMax: new RunningMax(SIGNAL_COUNT_TO_CALCULATE_MAX, 0.05),
            runningEMA: new RunningEMA(0, 10, 0.8),
            p: 0,
        };
        const recorderState$ = RecordingActivity.stateChanged$;
        const signalPower$ = RecordingActivity.audioPowerChanged$
            .pipe(scan<number, Result, Result>((result, p) => {
                const runningMax = result.runningMax;
                const runningEMA = result.runningEMA;
                // Fill the window first; once full, only advance it while voice is active
                const isWindowNotFull = runningMax.sampleCount < SIGNAL_COUNT_TO_CALCULATE_MAX;
                const shouldAppendMax = isWindowNotFull || this.isVoiceActive;
                if (shouldAppendMax)
                    runningMax.appendSample(p);
                runningEMA.appendSample(p);
                return { runningMax, runningEMA, p };
            }, initialResult));

        this.recorderStateChangedSubscription = recorderState$.subscribe(rs => {
            this.isVoiceActive = rs.isVoiceActive;
            this._isRecording = rs.isRecording;
        });
        const getHeight = (power: number, maxPower: number) => Math.floor((translate(power, [0, 0.6*maxPower], [MIN_HEIGHT, MAX_HEIGHT]))) * 100 / MAX_HEIGHT;
        this.signalPowerChangedSubscription = signalPower$.subscribe(({ runningMax, runningEMA }) => {
            if (!this._isRecording || !this.isVoiceActive || !this.isVisible)
                return;

            const maxPower = runningMax.value;
            const power1 = runningEMA.value;
            this.recentPowers.push(power1);
            if (this.recentPowers.length > 10)
                this.recentPowers.shift();

            const recentPowers = this.recentPowers;
            const targetHeight1 = getHeight(power1, maxPower);
            const power2 = recentPowers[recentPowers.length - 3] ?? 0;
            const targetHeight2 = Math.floor(clamp(0.6 * getHeight(power2, maxPower), MIN_HEIGHT, MAX_HEIGHT));
            const power3 = recentPowers[recentPowers.length - 5] ?? 0;
            const targetHeight3 = Math.floor(clamp(0.4 * getHeight(power3, maxPower), MIN_HEIGHT, MAX_HEIGHT));
            const MAX_HEIGHT_WITH_PADDING = MAX_HEIGHT - 4;
            this.audioPowerState = {
                height1: clamp(targetHeight1, MIN_HEIGHT, MAX_HEIGHT_WITH_PADDING),
                height2: clamp(targetHeight2, MIN_HEIGHT, MAX_HEIGHT_WITH_PADDING),
                height3: clamp(targetHeight3, MIN_HEIGHT, MAX_HEIGHT_WITH_PADDING),
            };
        });
    }

    disconnectedCallback() {
        super.disconnectedCallback();

        this.observer.disconnect();
        this.recorderStateChangedSubscription.unsubscribe();
        this.signalPowerChangedSubscription.unsubscribe();
    }

    // Audio power arrives at ~10Hz, and each update rewrites rect geometry - so
    // without this the bars alone put a change in ~1 frame in 6, on instants
    // unrelated to the animation grid. Lit coalesces the states meanwhile.
    protected async scheduleUpdate(): Promise<void> {
        await fastWriteRaf10Async();
        await super.scheduleUpdate();
    }

    // The rest-state bars animate inside the shadow root, where a document
    // sweep cannot reach them, and they are new nodes on every re-render.
    protected updated(): void {
        AnimationSync.syncAll(this.renderRoot);
    }

    protected render(): unknown {
        const { size, audioPowerState, isVoiceActive, isVisible } = this;
        const width = 10;
        if (!this._isRecording) {
            const offset = 50 - MIN_HEIGHT / 2;
            return html`
                <svg xmlns='http://www.w3.org/2000/svg' width='${size * 4}' height='${size * 4}'
                     preserveAspectRatio='none'
                     viewBox='0 0 24 24' fill='none' stroke='var(--white)'
                     stroke-width='${width}%' stroke-linecap='round' stroke-linejoin='bevel'>
                    <rect id='record-rect-2' class='in-rest' data-anim-sync
                          x='${width * 2.5}%'
                          y='${offset}%'
                          width='${width}%'
                          height='${MIN_HEIGHT}%'
                          fill='var(--white)' stroke-width='0' rx='5%' ry='5%'>
                    </rect>
                    <rect id='record-rect-3' class='in-rest' data-anim-sync
                          x='${width * 4.5}%'
                          y='${offset}%'
                          width='${width}%'
                          height='${MIN_HEIGHT}%'
                          fill='var(--white)' stroke-width='0' rx='5%' ry='5%'>
                    </rect>
                    <rect id='record-rect-4' class='in-rest' data-anim-sync
                          x='${width * 6.5}%'
                          y='${offset}%'
                          width='${width}%'
                          height='${MIN_HEIGHT}%'
                          fill='var(--white)' stroke-width='0' rx='5%' ry='5%'>
                    </rect>
                </svg>
            `;
        } else {
            // Gate on the reactive isVisible, not getComputedStyle(display): a plain
            // display:none -> flex reveal via ancestor classes is not a Lit input, so a
            // computed-style gate leaves a stale empty render until audio re-renders it.
            if (!this.isVisible)
                return html``;

            const shouldAnimate = isVoiceActive && isVisible;
            const height1 = shouldAnimate ? audioPowerState.height1 : MIN_HEIGHT;
            const height2 = shouldAnimate ? audioPowerState.height2 : MIN_HEIGHT;
            const height3 = shouldAnimate ? audioPowerState.height3 : MIN_HEIGHT;
            const offset1 = 50 - height1 / 2;
            const offset2 = 50 - height2 / 2;
            const offset3 = 50 - height3 / 2;

            const edgeDotCls = shouldAnimate ? 'active' : 'non-active';
            const centerDotCls = isVoiceActive || !isVisible ? '' : 'in-rest';

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
