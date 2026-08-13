import { customElement, property, state } from 'lit/decorators.js';
import { css, html, LitElement } from 'lit';
import { scan, Subscription } from 'rxjs';
import { clamp, RunningMax, translate } from 'math';
import { AnimationSync } from 'animation-sync';
import { fastWriteRaf10Async } from 'fast-raf';
import { RecordingActivity } from '../AudioRecorder/recording-activity';

const SIGNAL_COUNT_TO_CALCULATE_MAX = 100; // 100 * 96ms ~ 10s

interface Result {
    runningMax: RunningMax;
    p: number;
}

@customElement('narrow-recording-svg')
export class NarrowRecordingSvg extends LitElement {
    static styles = [css`
        :host {
            display: flex;
            overflow: visible;
        }
        div {
            display: flex;
            justify-content: center;
            align-self: center;
        }
        svg {
            overflow: visible;
        }
        path.weak {
            filter: drop-shadow(-2px -2px 4px rgba(179,77,174, 0.5)) drop-shadow(2px 2px 4px rgba(255,0,92, 0.5));
        }
        path.strong {
            filter: drop-shadow(-2px -2px 4px rgba(179,77,174, 0.75)) drop-shadow(2px 2px 4px rgba(255,0,92, 0.75));
        }
        path.idle-overlay {
            animation: wave 1.3s steps(13, start) infinite;
            will-change: opacity;
        }
        @keyframes wave {
            0%, 100% { opacity: 0; }
            50% { opacity: 1; }
        }
    `];

    private recorderStateChangedSubscription: Subscription;
    private signalPowerSubscription: Subscription;

    @state()
    private opacity: number | null = null;

    @state()
    private isVoiceActive = false;

    private _isRecording: boolean;
    @property({ type: Boolean })
    set isRecording(val: boolean) {
        if (val)
            this.opacity = null;
        this._isRecording = val;
    }

    get isRecording() { return this._isRecording };

    connectedCallback() {
        super.connectedCallback();

        const minOpacity = 60;
        const maxOpacity = 100;
        const initialResult: Result = {
            runningMax: new RunningMax(SIGNAL_COUNT_TO_CALCULATE_MAX, 0.05),
            p: 0,
        };
        const signalPower$ = RecordingActivity.audioPowerChanged$
            .pipe(scan<number, Result, Result>((result, p) => {
                const { runningMax } = result;
                // Fill the window first; once full, only advance it while voice is active
                const isWindowNotFull = runningMax.sampleCount < SIGNAL_COUNT_TO_CALCULATE_MAX;
                const shouldAppend = isWindowNotFull || this.isVoiceActive;
                if (shouldAppend)
                    runningMax.appendSample(p);
                return { runningMax, p };
            }, initialResult));

        this.recorderStateChangedSubscription = RecordingActivity.stateChanged$.subscribe(s => {
            this.isRecording = s.isRecording;
            this.isVoiceActive = s.isVoiceActive;
        });
        this.signalPowerSubscription = signalPower$.subscribe(({ runningMax, p }) => {
            if (!this._isRecording)
                return;

            if (this.opacity === null)
                runningMax.reset(); // On turn on recording

            const maxPower = runningMax.value;
            const maxSampleCount = runningMax.sampleCount;
            const maxNotYetCalculatedAdjustment = maxSampleCount < SIGNAL_COUNT_TO_CALCULATE_MAX / 2
                ? clamp(maxSampleCount / (SIGNAL_COUNT_TO_CALCULATE_MAX / 4), 0, 1)
                : 1;
            const power = p * maxNotYetCalculatedAdjustment;
            const opacity = translate(power, [0, 0.6*maxPower], [minOpacity, maxOpacity]) / maxOpacity;
            this.opacity = isNaN(opacity)
                ? minOpacity / 100
                : opacity;
        });
    }

    disconnectedCallback() {
        super.disconnectedCallback();

        this.recorderStateChangedSubscription.unsubscribe();
        this.signalPowerSubscription.unsubscribe();
    }

    // Same ~10Hz audio-power stream as active-recording-svg, same reason to land
    // its writes on the animation grid rather than on instants of its own.
    protected async scheduleUpdate(): Promise<void> {
        await fastWriteRaf10Async();
        await super.scheduleUpdate();
    }

    // The idle overlay animates inside the shadow root, out of reach of a document
    // sweep, and is a new node on every re-render.
    protected updated(): void {
        AnimationSync.syncAll(this.renderRoot);
    }

    protected render(): unknown {
        if (!this._isRecording)
            return this.renderIcon(0.75, 'idle-overlay', '');

        if (getComputedStyle(this).display === 'none')
            return html``;

        const opacity = this.opacity ?? 0.7;
        return this.renderIcon(1, '', `opacity: ${opacity};`);
    }

    // Private methods

    private static readonly PATH_D = 'M8.62668 6.97296C8.62668 5.64058 9.8962 4.25461 11.8813 4.25461C13.8664 4.25461 15.1359 5.64058 15.1359 6.97296V14.6472C15.1359 15.866 13.8903 17.2731 11.8813 17.2731C9.8962 17.2731 8.62668 15.8871 8.62668 14.5547V6.97296ZM11.8813 1C8.46512 1 5.37207 3.49738 5.37207 6.97296V14.5547C5.37207 18.0303 8.46512 20.5277 11.8813 20.5277C15.2736 20.5277 18.3905 18.0514 18.3905 14.6472V6.97296C18.3905 3.49738 15.2975 1 11.8813 1ZM0.95509 17.4257C1.77346 17.0542 2.73803 17.4165 3.1095 18.2349C4.62287 21.5689 7.98144 23.886 11.8634 23.886C15.7134 23.886 19.0486 21.6069 20.5796 18.3167C20.9588 17.5019 21.9267 17.1487 22.7416 17.5279C23.5564 17.9071 23.9096 18.875 23.5304 19.6898C21.6619 23.7052 17.7755 26.6066 13.1745 27.0743V30.234C13.1745 31.1899 12.3996 31.9648 11.4437 31.9648C10.4878 31.9648 9.71289 31.1899 9.71289 30.234V26.9609C5.44063 26.2391 1.8819 23.4045 0.145911 19.5801C-0.225565 18.7617 0.136718 17.7972 0.95509 17.4257Z';

    private renderIcon(fillOpacity: number, overlayExtraClass: string, overlayStyle: string): unknown {
        const d = NarrowRecordingSvg.PATH_D;
        return html`
            <svg width='24' height='24' viewBox='0 0 24 32'
                 xmlns='http://www.w3.org/2000/svg'>
                <path class='weak'
                      fill='#FF3880' fill-opacity='${fillOpacity}' fill-rule='evenodd' clip-rule='evenodd'
                      d='${d}' />
                <path class='strong ${overlayExtraClass}' data-anim-sync
                      fill='#FF3880' fill-opacity='${fillOpacity}' fill-rule='evenodd' clip-rule='evenodd'
                      style='${overlayStyle}'
                      d='${d}' />
            </svg>
        `;
    }
}
