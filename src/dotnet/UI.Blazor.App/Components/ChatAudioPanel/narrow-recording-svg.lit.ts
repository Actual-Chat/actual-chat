import { customElement } from 'lit/decorators.js';
import { css, html, LitElement } from 'lit';
import { animationFrameScheduler, map, scan, Observable, Subscription } from 'rxjs';
import { throttleTime } from 'rxjs/operators';
import { OpusMediaRecorder } from '../AudioRecorder/opus-media-recorder';
import { RunningMax, translate } from 'math';
import { observe } from '../../Services/observe-directive-lit';

const SIGNAL_COUNT_TO_CALCULATE_MAX = 200; // 200 * 30ms = 6s

type Result = {
    runningMax: RunningMax;
    p: number;
    i: number;
}

@customElement('narrow-recording-svg')
class NarrowRecordingSvg extends LitElement {
    static styles = [css`
        :host {
            display: flex;
        }
        div {
            display: flex;
            justify-content: center;
            align-self: center;
        }
    `];

    private readonly opacity$: Observable<number>;
    private readonly recorderStateChangedSubscription: Subscription;

    private isVoiceActive = false;
    private isRecording = false;
    private lastIsRecording = false;

    constructor() {
        super();

        const minOpacity = 60;
        const maxOpacity = 100;
        const signalPower$ = OpusMediaRecorder.audioPowerChanged$
            .pipe(throttleTime(0, animationFrameScheduler));

        this.recorderStateChangedSubscription = OpusMediaRecorder.recorderStateChanged$.subscribe(s => {
            this.isVoiceActive = s.isVoiceActive;
            this.isRecording = s.isRecording;
        });

        this.opacity$ = signalPower$
            .pipe(scan<number, Result, RunningMax>((runningMaxOrResult, p, i) => {
                const runningMax: RunningMax = runningMaxOrResult['runningMax'] || runningMaxOrResult;
                const { isRecording, isVoiceActive, lastIsRecording } = this;
                if (isRecording != lastIsRecording) {
                    // cleanup state on start/stop recording
                    this.lastIsRecording = isRecording;
                    runningMax.reset();
                }
                if (!isVoiceActive)
                    return { runningMax, p, i};

                runningMax.appendSample(p);
                return { runningMax, p, i };
            }, new RunningMax(SIGNAL_COUNT_TO_CALCULATE_MAX, 0)))
            .pipe(map(({ runningMax, p }) => {
                const maxPower = runningMax.value;
                const maxSampleCount = runningMax.sampleCount;
                if (maxSampleCount < SIGNAL_COUNT_TO_CALCULATE_MAX / 2) // beginning of recording
                    return Math.floor(minOpacity + Math.random() * minOpacity);

                const opacity = Math.floor((translate(p, [0, 0.8 * maxPower], [minOpacity, maxOpacity]))) / maxOpacity;
                return (!this.isVoiceActive || isNaN(opacity))
                    ? minOpacity / 100
                    : opacity;
            }));
    }

    disconnectedCallback() {
        super.disconnectedCallback();
        this.recorderStateChangedSubscription.unsubscribe();
    }

    protected render(): unknown {
        const defaultOpacity = 60;
        const opacity = observe(this.opacity$, defaultOpacity);

        return html`
            <svg width="24" height="24" viewBox="0 0 24 32"
                 fill="var(--danger)" fill-opacity="${opacity}"
                 xmlns="http://www.w3.org/2000/svg">
                <path fill="var(--danger)" fill-rule="evenodd" clip-rule="evenodd" d="M8.62668 6.97296C8.62668 5.64058 9.8962 4.25461 11.8813 4.25461C13.8664 4.25461 15.1359 5.64058 15.1359 6.97296V14.6472C15.1359 15.866 13.8903 17.2731 11.8813 17.2731C9.8962 17.2731 8.62668 15.8871 8.62668 14.5547V6.97296ZM11.8813 1C8.46512 1 5.37207 3.49738 5.37207 6.97296V14.5547C5.37207 18.0303 8.46512 20.5277 11.8813 20.5277C15.2736 20.5277 18.3905 18.0514 18.3905 14.6472V6.97296C18.3905 3.49738 15.2975 1 11.8813 1ZM0.95509 17.4257C1.77346 17.0542 2.73803 17.4165 3.1095 18.2349C4.62287 21.5689 7.98144 23.886 11.8634 23.886C15.7134 23.886 19.0486 21.6069 20.5796 18.3167C20.9588 17.5019 21.9267 17.1487 22.7416 17.5279C23.5564 17.9071 23.9096 18.875 23.5304 19.6898C21.6619 23.7052 17.7755 26.6066 13.1745 27.0743V30.234C13.1745 31.1899 12.3996 31.9648 11.4437 31.9648C10.4878 31.9648 9.71289 31.1899 9.71289 30.234V26.9609C5.44063 26.2391 1.8819 23.4045 0.145911 19.5801C-0.225565 18.7617 0.136718 17.7972 0.95509 17.4257Z"/>
            </svg>
        `;
    }
}
