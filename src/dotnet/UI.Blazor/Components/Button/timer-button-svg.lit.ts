import {customElement, property} from "lit/decorators.js";
import {css, html, LitElement} from "lit";

@customElement('timer-button-svg')
class TimerButtonSvg extends LitElement {
    @property({ type: String })
    sizeClass = "";
    @property({ type: Number })
    timing = 0;

    static styles = [css`
        :host {
        }
        .timer-content {
            width: 2.5rem;
            height: 2.5rem;
        }
        .timer-content.btn-sm {
            width: 2rem;
            height: 2rem;
        }
        .timer-content {
            position: relative;

            .c-border {
                width: 100%;
                height: 100%;
            }
            svg {
                width: 100%;
                height: 100%;
                .circle {
                    stroke-dashoffset: 0;
                    stroke-dasharray: 300;
                    stroke-width: 1.2;
                }
            }
        }

        @keyframes dash {
            from {
                stroke-dashoffset: 200;
            }
            to {
                stroke-dashoffset: 300;
            }
        }
    `];

    protected render(): unknown {
        let animateFunc = `animation: dash ${this.timing}s linear forwards`;
        return html`
            <div class="timer-content ${this.sizeClass}">
                <div class="c-border">
                    <svg viewBox="0 0 32 32">
                        <circle class="circle" r="15" cx="16" cy="16" transform='rotate(-90 16 16)' fill="transparent" stroke="var(--toast-text)" style="${animateFunc}"></circle>
                    </svg>
                </div>
            </div>
        `;
    }
}
