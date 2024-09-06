import { AsyncDirective } from 'lit/async-directive.js';
import { directive } from 'lit/directive.js';
import { customElement, property } from 'lit/decorators.js';
import { css, html, LitElement } from 'lit';
import { animationFrameScheduler, map, Observable, Subscription } from 'rxjs';
import { delay, throttleTime } from 'rxjs/operators';
import { OpusMediaRecorder } from '../AudioRecorder/opus-media-recorder';

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
    @property({ type: Boolean })
    isVoiceActive = false;
    @property()
    edgeDotCls = "";
    @property()
    centerDotCls = "";
    @property({ type: Boolean })
    isFirstLoading = true;

    protected render(): unknown {
        const size = this.size;
        const width = 10;
        const minHeight = 20;
        const maxHeight = 100;
        let signalPower$ = OpusMediaRecorder.audioPowerChanged$
            .pipe(throttleTime(0, animationFrameScheduler));
        let maxPower = 0;

        this.edgeDotCls = this.isVoiceActive ? "active" : "non-active";
        this.centerDotCls = this.isVoiceActive ? "" : "in-rest";

        if (this.isFirstLoading && this.isVoiceActive) {
            this.isFirstLoading = false;
        }

        // height$ in percent
        const height1$ = signalPower$
            .pipe(map(p => {
                if (p > maxPower)
                    maxPower = p;
                let height = Math.floor((translate(p, [0, maxPower], [minHeight, maxHeight]))) / maxHeight * 90;
                if (this.isVoiceActive && height > minHeight) {
                    return isNaN(height) ? minHeight : height;
                } else {
                    return minHeight;
                }
            }));

        const height2$ = signalPower$
            .pipe(
                delay(150),
                map(p => {
                    if (p > maxPower)
                        maxPower = p;
                    let height = Math.floor((translate(p, [0, 0.8 * maxPower], [minHeight, maxHeight]))) / maxHeight * 70;
                    if (this.isVoiceActive && height > minHeight) {
                        return isNaN(height) ? minHeight : height;
                    } else {
                        return minHeight;
                    }
                }));

        const height3$ = signalPower$
            .pipe(
                delay(300),
                map(p => {
                    if (p > maxPower)
                        maxPower = p;
                    let height = Math.floor((translate(p, [0, 0.8*maxPower], [minHeight, maxHeight]))) / maxHeight * 50;
                    if (this.isVoiceActive && height > minHeight) {
                        return isNaN(height) ? minHeight : height;
                    } else {
                        return minHeight;
                    }
                }));

        // offsets in percent
        const offset1$ = height1$
            .pipe(map(h => {
                return isNaN(h) ? 50 - minHeight / 2 : 50 - h / 2;
            }));
        const offset2$ = height2$
            .pipe(map(h => {
                return isNaN(h) ? 50 - minHeight / 2 : 50 - h / 2;
            }));
        const offset3$ = height3$
            .pipe(map(h => {
                return isNaN(h) ? 50 - minHeight / 2 : 50 - h / 2;
            }));

        let height1 = observe(height1$);
        let height2 = observe(height2$);
        let height3 = observe(height3$);
        let offset1 = observe(offset1$);
        let offset2 = observe(offset2$);
        let offset3 = observe(offset3$);

        return html`
            <svg xmlns="http://www.w3.org/2000/svg" width="${size * 4}" height="${size * 4}"
                 preserveAspectRatio="none"
                 viewBox="0 0 24 24" fill="none" stroke="var(--white)"
                 stroke-width="${width}%" stroke-linecap="round" stroke-linejoin="bevel">
                <rect id="record-rect-1" class="${this.edgeDotCls}"
                      x="${width / 2}%"
                      y="${this.isFirstLoading ? 50 - minHeight / 2 : offset3}%"
                      width="${width}%"
                      height="${this.isFirstLoading ? minHeight : height3}%"
                      fill="var(--white)" stroke-width="0" rx="5%" ry="5%">
                </rect>
                <rect id="record-rect-2" class="${this.centerDotCls}"
                      x="${width * 2.5}%"
                      y="${this.isFirstLoading ? 50 - minHeight / 2 : offset2}%"
                      width="${width}%"
                      height="${this.isFirstLoading ? minHeight : height2}%"
                      fill="var(--white)" stroke-width="0" rx="5%" ry="5%">
                </rect>
                <rect id="record-rect-3" class="${this.centerDotCls}"
                      x="${width * 4.5}%"
                      y="${this.isFirstLoading ? 50 - minHeight / 2 : offset1}%"
                      width="${width}%"
                      height="${this.isFirstLoading ? minHeight : height1}%"
                      fill="var(--white)" stroke-width="0" rx="5%" ry="5%">
                </rect>
                <rect id="record-rect-4" class="${this.centerDotCls}"
                      x="${width * 6.5}%"
                      y="${this.isFirstLoading ? 50 - minHeight / 2 : offset2}%"
                      width="${width}%"
                      height="${this.isFirstLoading ? minHeight : height2}%"
                      fill="var(--white)" stroke-width="0" rx="5%" ry="5%">
                </rect>
                <rect id="record-rect-5" class="${this.edgeDotCls}"
                      x="${width * 8.5}%"
                      y="${this.isFirstLoading ? 50 - minHeight / 2 : offset3}%"
                      width="${width}%"
                      height="${this.isFirstLoading ? minHeight : height3}%"
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
