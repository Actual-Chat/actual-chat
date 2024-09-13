import { AsyncDirective } from 'lit/async-directive.js';
import { directive } from 'lit/directive.js';
import { customElement, property } from 'lit/decorators.js';
import { css, html, LitElement } from 'lit';
import { animationFrameScheduler, map, scan, Observable, Subscription } from 'rxjs';
import { delayWhen, throttleTime, skip, take, distinctUntilChanged } from 'rxjs/operators';
import { OpusMediaRecorder } from '../AudioRecorder/opus-media-recorder';
import { clamp, RunningMax } from 'math';

const SIGNAL_COUNT_TO_CALCULATE_MAX = 200; // 200 * 30ms = 6s

type Result = {
    runningMax: RunningMax;
    p: number;
    i: number;
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
            animation: in-rest 1.25s linear infinite 0.60s;
        }
        rect#record-rect-3.in-rest {
            animation: in-rest 1.25s linear infinite 0.40s;
        }
        rect#record-rect-4.in-rest {
            animation: in-rest 1.25s linear infinite 0.20s;
        }

        @keyframes in-rest {
            0% {
                transform: translateY(0px);
                opacity: 1;
            }
            25% {
                transform: translateY(2px);
                opacity: 0.9;
            }
            50% {
                transform: translateY(0px);
                opacity: 0.8;
            }
            75% {
                transform: translateY(-2px);
                opacity: 0.7;
            }
            100% {
                transform: translateY(0px);
                opacity: 0.6;
            }
        }
    `];

    @property()
    size = 10;

    private readonly height1$: Observable<number>;
    private readonly height2$: Observable<number>;
    private readonly height3$: Observable<number>;
    private readonly offset1$: Observable<number>;
    private readonly offset2$: Observable<number>;
    private readonly offset3$: Observable<number>;
    private readonly isVoiceActive$: Observable<boolean>;
    private readonly recorderStateChangedSubscription: Subscription;

    private isVoiceActive = false;
    private isRecording = false;
    private lastIsRecording = false;

    constructor() {
        super();

        const minHeight = 10;
        const maxHeight = 100;
        this.isVoiceActive$ = OpusMediaRecorder.recorderStateChanged$
            .pipe(map(s => s.isVoiceActive), distinctUntilChanged());
        const signalPower$ = OpusMediaRecorder.audioPowerChanged$
            .pipe(throttleTime(0, animationFrameScheduler));

        this.recorderStateChangedSubscription = OpusMediaRecorder.recorderStateChanged$.subscribe(s => {
           this.isVoiceActive = s.isVoiceActive;
           this.isRecording = s.isRecording;
        });

        this.height1$ = signalPower$
            .pipe(scan<number, Result, RunningMax>((runningMaxOrResult, p, i) => {
                const runningMax: RunningMax = runningMaxOrResult['runningMax'] || runningMaxOrResult;
                const { isRecording, isVoiceActive, lastIsRecording } = this;
                if (isRecording != lastIsRecording) {
                    // cleanup state on start/stop recording
                    this.lastIsRecording = isRecording;
                    runningMax.reset();
                }
                if (!isVoiceActive)
                    return { runningMax, p, i };

                runningMax.appendSample(p);
                return { runningMax, p, i };
            }, new RunningMax(SIGNAL_COUNT_TO_CALCULATE_MAX, 0)))
            .pipe(map(({ runningMax, p }) => {
                const maxPower = runningMax.value;
                const maxSampleCount = runningMax.sampleCount;
                if (maxSampleCount < SIGNAL_COUNT_TO_CALCULATE_MAX / 2) // beginning of recording
                    return Math.floor(minHeight + Math.random() * minHeight);

                const height = Math.floor((translate(p, [0, 0.8 * maxPower], [minHeight, maxHeight]))) * 100 / maxHeight;
                return (!this.isVoiceActive || isNaN(height))
                    ? minHeight
                    : height;
            }));

        this.height2$ = this.height1$
            .pipe(map(h => clamp(0.7 * h, minHeight, maxHeight)))
            .pipe(delayWhen(() => this.height1$.pipe(skip(5), take(1)))); // with 150 ms delay
        this.height3$ = this.height1$
            .pipe(map(h => clamp(0.4 * h, minHeight, maxHeight)))
            .pipe(delayWhen(() => this.height1$.pipe(skip(10), take(1)))); // with 300 ms delay

        // offsets in percent
        this.offset1$ = this.height1$
            .pipe(map(h => 50 - h / 2));
        this.offset2$ = this.height2$
            .pipe(map(h => 50 - h / 2));
        this.offset3$ = this.height3$
            .pipe(map(h => 50 - h / 2));
    }

    disconnectedCallback() {
        super.disconnectedCallback();

        this.recorderStateChangedSubscription.unsubscribe();
    }

    protected render(): unknown {
        const { size } = this;
        const width = 10;

        const height1 = observe(this.height1$);
        const height2 = observe(this.height2$);
        const height3 = observe(this.height3$);
        const offset1 = observe(this.offset1$);
        const offset2 = observe(this.offset2$);
        const offset3 = observe(this.offset3$);
        const edgeDotCls = observe(this.isVoiceActive$.pipe(map(b => b ? "active" : "non-active")));
        const centerDotCls = observe(this.isVoiceActive$.pipe(map(b => b ? "" : "in-rest")));

        return html`
            <svg xmlns="http://www.w3.org/2000/svg" width="${size * 4}" height="${size * 4}"
                 preserveAspectRatio="none"
                 viewBox="0 0 24 24" fill="none" stroke="var(--white)"
                 stroke-width="${width}%" stroke-linecap="round" stroke-linejoin="bevel">
                <rect id="record-rect-1" class="${edgeDotCls}"
                      x="${width / 2}%"
                      y="${offset3}%"
                      width="${width}%"
                      height="${height3}%"
                      fill="var(--white)" stroke-width="0" rx="5%" ry="5%">
                </rect>
                <rect id="record-rect-2" class="${centerDotCls}"
                      x="${width * 2.5}%"
                      y="${offset2}%"
                      width="${width}%"
                      height="${height2}%"
                      fill="var(--white)" stroke-width="0" rx="5%" ry="5%">
                </rect>
                <rect id="record-rect-3" class="${centerDotCls}"
                      x="${width * 4.5}%"
                      y="${offset1}%"
                      width="${width}%"
                      height="${height1}%"
                      fill="var(--white)" stroke-width="0" rx="5%" ry="5%">
                </rect>
                <rect id="record-rect-4" class="${centerDotCls}"
                      x="${width * 6.5}%"
                      y="${offset2}%"
                      width="${width}%"
                      height="${height2}%"
                      fill="var(--white)" stroke-width="0" rx="5%" ry="5%">
                </rect>
                <rect id="record-rect-5" class="${edgeDotCls}"
                      x="${width * 8.5}%"
                      y="${offset3}%"
                      width="${width}%"
                      height="${height3}%"
                      fill="var(--white)" stroke-width="0" rx="5%" ry="5%">
                </rect>
            </svg>
        `;
    }
}

class ObserveDirective extends AsyncDirective {
    #subscription: Subscription;

    render(observable: Observable<unknown>) {
        this.#subscription = observable.subscribe(value => this.setValue(value));
        return ``;
    }

    disconnected() {
        this.#subscription?.unsubscribe();
    }
}

const observe = directive(ObserveDirective);
const translate = (number: number, [inMin, inMax]: Array<number>, [outMin, outMax]: Array<number>) => {
    return (number - inMin) / (inMax - inMin) * (outMax - outMin) + outMin;
}
