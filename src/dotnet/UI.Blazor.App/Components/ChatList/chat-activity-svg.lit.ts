import {customElement, property} from "lit/decorators.js";
import {css, html, LitElement} from "lit";

@customElement('chat-activity-svg')
class ChatActivitySvg extends LitElement {
    static styles = [css`
        :host {
            display: flex;
        }
    `];
    @property()
    size = 4;
    @property({ type: Boolean })
    isActive = false;
    @property()
    activeColor = "text-02"
    @property()
    inactiveColor = "text-02"

    protected render(): unknown {
        if (this.isActive) {
            return html`
                <svg xmlns="http://www.w3.org/2000/svg" width="${this.size * 4}" height="${this.size * 4}" viewBox="0 0 24 24" fill="none" stroke="var(--${this.activeColor})" stroke-width="2" stroke-linecap="butt" stroke-linejoin="bevel">
                    <polygon id="stream-svg-polygon" points="11 5 6 9 2 9 2 15 6 15 11 19 11 5">
                        <animate
                            href="#stream-svg-polygon"
                            attributeName="points"
                            attributeType="XML"
                            values="11 5 6 7 2 9 2 15 6 17 11 19 11 5;">
                        </animate>
                    </polygon>
                    <path id="stream-svg-1" d="M14.54 7.46a5 6 0 0 1 0 9.07" stroke-linecap="round">
                        <animate
                            href="#stream-svg-1"
                            attributeName="opacity"
                            repeatCount="indefinite"
                            dur="2s"
                            values="0; 0; 1; 1;">
                        </animate>
                    </path>
                    <path id="stream-svg-2" d="M17.54 5.46a5 7 0 0 1 0 13.07" stroke-linecap="round">
                        <animate
                            href="#stream-svg-2"
                            attributeName="opacity"
                            repeatCount="indefinite"
                            dur="2s"
                            values="0; 0; 0; 1;">
                        </animate>
                    </path>
                </svg>
            `;
        } else {
            return html`
                <svg xmlns="http://www.w3.org/2000/svg" width="${this.size * 4}" height="${this.size * 4}" viewBox="0 0 24 24" fill="none" stroke="var(--${this.inactiveColor})" stroke-width="2" stroke-linecap="butt" stroke-linejoin="bevel">
                    <polygon id="stream-svg-polygon" points="11 5 6 9 2 9 2 15 6 15 11 19 11 5">
                        <animate
                            href="#stream-svg-polygon"
                            attributeName="points"
                            attributeType="XML"
                            values="11 5 6 7 2 9 2 15 6 17 11 19 11 5;">
                        </animate>
                    </polygon>
                    <path id="stream-svg-1" d="M14.54 10.46a5 0 0 0 1 0 3" stroke-linecap="round">
                        <animate
                            href="#stream-svg-1"
                            attributeName="opacity"
                            repeatCount="indefinite"
                            dur="2s"
                            values="1; 1; 1; 1;">
                        </animate>
                    </path>
                    <path id="stream-svg-2" d="M18.24 10.46a5 0 0 0 1 0 3" stroke-linecap="round">
                        <animate
                            href="#stream-svg-2"
                            attributeName="opacity"
                            repeatCount="indefinite"
                            dur="2s"
                            values="1; 1; 1; 1;">
                        </animate>
                    </path>
                </svg>
            `;
        }
    }
}
