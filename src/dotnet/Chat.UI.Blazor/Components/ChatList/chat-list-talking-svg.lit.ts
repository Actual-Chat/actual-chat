import {customElement, property} from "lit/decorators.js";
import {css, html, LitElement} from "lit";

@customElement('chat-list-talking-svg')
class ChatListTalkingSvg extends LitElement {
    protected render(): unknown {
        return html`
            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="-2 -2 30 30" fill="none" stroke="#444444" stroke-width="2" stroke-linecap="butt" stroke-linejoin="bevel">
                <polygon id="talk-svg-polygon" points="11 5 6 9 2 9 2 15 6 15 11 19 11 5">
                    <animate
                        href="#talk-svg-polygon"
                        attributeName="points"
                        attributeType="XML"
                        dur="1s"
                        repeatCount="indefinite"
                        values="
                            11 5 6 9 2 9 2 15 6 15 11 19 11 5;
                            11 5 6 7 2 9 2 15 6 17 11 19 11 5;
                            11 5 6 9 2 9 2 15 6 15 11 19 11 5;">
                    </animate>
                </polygon>
                <path id="talk-svg-1" d="M14.54 8.46a5 5 0 0 1 0 7.07">
                    <animate
                        href="#talk-svg-1"
                        attributeName="opacity"
                        repeatCount="indefinite"
                        dur="2s"
                        values="0; 1; 1; 1;">
                    </animate>
                </path>
                <path id="talk-svg-2" d="M18.54 7.46a5 6 0 0 1 0 9.07">
                    <animate
                        href="#talk-svg-2"
                        attributeName="opacity"
                        repeatCount="indefinite"
                        dur="2s"
                        values="0; 0; 1; 1;">
                    </animate>
                </path>
                <path id="talk-svg-3" d="M22.54 6.46a5 7 0 0 1 0 11.07">
                    <animate
                        href="#talk-svg-3"
                        attributeName="opacity"
                        repeatCount="indefinite"
                        dur="2s"
                        values="0; 0; 0; 1;">
                    </animate>
                </path>
            </svg>
        `;
    }
}
