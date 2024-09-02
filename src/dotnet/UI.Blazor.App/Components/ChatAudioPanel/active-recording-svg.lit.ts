import { AsyncDirective } from 'lit/async-directive.js';
import { Directive, directive, EventPart, DirectiveParameters } from 'lit/directive.js';
import { customElement, property } from 'lit/decorators.js';
import { css, html, LitElement } from 'lit';
import { Observable, Subscription, map, animationFrameScheduler } from 'rxjs';
import { throttleTime } from 'rxjs/operators';
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
    `];
    @property()
    size = 10;

    protected render(): unknown {
        const size = this.size;
        const width = 10;
        const minHeight = 4;
        const maxHeight = size * 4;
        const signalPower$ = OpusMediaRecorder.audioPowerChanged$
            .pipe(throttleTime(0, animationFrameScheduler));
        let maxPower = 0;
        const height$ = signalPower$
            .pipe(map(p => {
                if (p > maxPower)
                    maxPower = p;
                return Math.floor(translate(p, [0, 0.8*maxPower], [minHeight, maxHeight]));
            }));

        return html`
            <svg xmlns="http://www.w3.org/2000/svg" width="${size * 4}" height="${observe(height$)}"
                 preserveAspectRatio="none"
                 viewBox="0 0 24 24" fill="none" stroke="var(--white)"
                 stroke-width="${width}%" stroke-linecap="round" stroke-linejoin="bevel">
                <line id="recording-line-1" x1="${width}%" x2="${width}%" y1="50%" y2="50%">
                    <animate
                        attributeName="y1"
                        values="20%; 40%; 60%; 80%; 60%; 40%; 20%;"
                        repeatCount="indefinite"
                        begin="0.1s"
                        dur="2.1s">
                    </animate>
                    <animate
                        attributeName="y2"
                        values="80%; 60%; 40%; 20%; 40%; 60%; 80%;"
                        repeatCount="indefinite"
                        begin="0.1s"
                        dur="2.1s">
                    </animate>
                </line>
                <line id="recording-line-2" x1="${width * 3}%" x2="${width * 3}%" y1="40%" y2="60%">
                    <animate
                        attributeName="y1"
                        values="40%; 60%; 80%; 60%; 40%; 20%; 40%;"
                        repeatCount="indefinite"
                        begin="0.1s"
                        dur="2.1s">
                    </animate>
                    <animate
                        attributeName="y2"
                        values="60%; 40%; 20%; 40%; 60%; 80%; 60%;"
                        repeatCount="indefinite"
                        begin="0.1s"
                        dur="2.1s">
                    </animate>
                </line>
                <line id="recording-line-3" x1="${width * 5}%" x2="${width * 5}%" y1="40%" y2="60%">
                    <animate
                        attributeName="y1"
                        values="60%; 80%; 60%; 40%; 20%; 40%; 60%;"
                        repeatCount="indefinite"
                        begin="0.1s"
                        dur="2.1s">
                    </animate>
                    <animate
                        attributeName="y2"
                        values="40%; 20%; 40%; 60%; 80%; 60%; 40%"
                        repeatCount="indefinite"
                        begin="0.1s"
                        dur="2.1s">
                    </animate>
                </line>
                <line id="recording-line-4" x1="${width * 7}%" x2="${width * 7}%" y1="40%" y2="60%">
                    <animate
                        attributeName="y1"
                        values="40%; 60%; 80%; 60%; 40%; 20%; 40%;"
                        repeatCount="indefinite"
                        begin="0.1s"
                        dur="2.1s">
                    </animate>
                    <animate
                        attributeName="y2"
                        values="60%; 40%; 20%; 40%; 60%; 80%; 60%;"
                        repeatCount="indefinite"
                        begin="0.1s"
                        dur="2.1s">
                    </animate>
                </line>
                <line id="recording-line-5" x1="${width * 9}%" x2="${width * 9}%" y1="50%" y2="50%">
                    <animate
                        attributeName="y1"
                        values="20%; 40%; 60%; 80%; 60%; 40%; 20%;"
                        repeatCount="indefinite"
                        begin="0.1s"
                        dur="2.1s">
                    </animate>
                    <animate
                        attributeName="y2"
                        values="80%; 60%; 40%; 20%; 40%; 60%; 80%;"
                        repeatCount="indefinite"
                        begin="0.1s"
                        dur="2.1s">
                    </animate>
                </line>
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
