import {customElement, property} from "lit/decorators.js";
import {css, html, LitElement} from "lit";

@customElement('active-recording-svg')
class ActiveRecordingSvg extends LitElement {
    static styles = [css`
        :host {
            display: flex;
        }
    `];
    @property()
    size = 10;

    protected render(): unknown {
        const size = this.size;
        const width = 10;
        return html`
            <svg xmlns="http://www.w3.org/2000/svg" width="${size * 4}" height="${size * 4}"
                 viewBox="0 0 24 24" fill="none" stroke="var(--white)"
                 stroke-width="${width}%" stroke-linecap="round" stroke-linejoin="bevel">
                <line id="recording-line-1" x1="${width}%" x2="${width}%" y1="20%" y2="80%">
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
                <line id="recording-line-3" x1="${width * 5}%" x2="${width * 5}%" y1="60%" y2="40%">
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
                <line id="recording-line-5" x1="${width * 9}%" x2="${width * 9}%" y1="20%" y2="80%">
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
