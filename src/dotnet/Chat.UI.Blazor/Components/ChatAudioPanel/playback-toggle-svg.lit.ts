import {customElement, property} from "lit/decorators.js";
import {css, html, LitElement} from "lit";

@customElement('playback-toggle-svg')
class PlaybackToggleSvg extends LitElement {
    @property()
    size = "24";
    @property()
    viewBox = "0 0 24 24";
    @property({ type: Boolean })
    isAnimated = false;
    @property({ type: Boolean })
    isOn = false;

    protected render(): unknown {
        if (this.isOn)
            return html`
                <svg class="spin-and-on" width="${this.size}" height="${this.size}" viewBox="${this.viewBox}" fill="${this.isOn ? "#007AFF" : "#898989"}" xmlns="http://www.w3.org/2000/svg">
                    <path id="playback-toggle-3" d="M12 3C6.5 3 2 7.7 2 13.4V18C2 20.2 3.8 22 6 22C6.7 22 7.3 21.8 7.8 21.5C7.9 21.5 7.9 21.4 8 21.4C8.3 21.2 8.5 20.9 8.5 20.5V15.3C8.5 14.9 8.3 14.6 8 14.5C7.9 14.5 7.9 14.4 7.8 14.4C7.3 14.2 6.6 14 6 14C5.3 14 4.6 14.2 4 14.6V13.4C4 8.8 7.6 5 12 5C16.4 5 20 8.8 20 13.4V14.6C19.4 14.3 18.7 14 18 14C17.4 14 16.7 14.2 16.2 14.4C16.1 14.4 16.1 14.5 16 14.5C15.7 14.7 15.5 15 15.5 15.3V20.5C15.5 20.9 15.7 21.2 16 21.4C16.1 21.4 16.1 21.5 16.2 21.5C16.7 21.8 17.4 22 18 22C20.2 22 22 20.2 22 18V13.4C22 7.7 17.5 3 12 3ZM6 16C6.2 16 6.3 16 6.4 16.1V20C6.3 20 6.2 20 6 20C4.9 20 4 19.1 4 18C4 16.9 4.9 16 6 16ZM18 20C17.8 20 17.7 20 17.6 19.9V16C17.7 16 17.9 15.9 18 15.9C19.1 15.9 20 16.8 20 17.9C20 19 19.1 20 18 20Z">
                        <animateTransform
                            href="#playback-toggle-3"
                            attributeName="transform"
                            type="rotate"
                            attributeType="XML"
                            dur="0.25s"
                            repeatCount="2"
                            values="
                                0 12 12;
                                360 12 12"
                        />
                        <animate
                            href="#playback-toggle-3"
                            attributeName="fill"
                            dur="0.5s"
                            repeatCount="1"
                            values="
                                #898989FF;
                                #898989FF;
                                #4582C4FF;
                                #227EE2FF;
                                #007AFF;"
                        />
                    </path>
                </svg>

                <style>
                    .spin-and-on {
                        animation: spin-and-on ease-in-out;
                        animation-duration: 0.5s;
                    }

                    @keyframes spin-and-on {
                        0% {
                            scale: 1;
                        }
                        50% {
                            scale: 0;
                        }
                        100% {
                            scale: 1;
                        }
                    }
                </style>
            `

        if (!this.isAnimated)
            return html`
                <svg width="${this.size}" height="${this.size}" viewBox="${this.viewBox}" fill="#898989" xmlns="http://www.w3.org/2000/svg">
                    <path id="playback-toggle-2" d="M12 3C6.5 3 2 7.7 2 13.4V18C2 20.2 3.8 22 6 22C6.7 22 7.3 21.8 7.8 21.5C7.9 21.5 7.9 21.4 8 21.4C8.3 21.2 8.5 20.9 8.5 20.5V15.3C8.5 14.9 8.3 14.6 8 14.5C7.9 14.5 7.9 14.4 7.8 14.4C7.3 14.2 6.6 14 6 14C5.3 14 4.6 14.2 4 14.6V13.4C4 8.8 7.6 5 12 5C16.4 5 20 8.8 20 13.4V14.6C19.4 14.3 18.7 14 18 14C17.4 14 16.7 14.2 16.2 14.4C16.1 14.4 16.1 14.5 16 14.5C15.7 14.7 15.5 15 15.5 15.3V20.5C15.5 20.9 15.7 21.2 16 21.4C16.1 21.4 16.1 21.5 16.2 21.5C16.7 21.8 17.4 22 18 22C20.2 22 22 20.2 22 18V13.4C22 7.7 17.5 3 12 3ZM6 16C6.2 16 6.3 16 6.4 16.1V20C6.3 20 6.2 20 6 20C4.9 20 4 19.1 4 18C4 16.9 4.9 16 6 16ZM18 20C17.8 20 17.7 20 17.6 19.9V16C17.7 16 17.9 15.9 18 15.9C19.1 15.9 20 16.8 20 17.9C20 19 19.1 20 18 20Z" fill="#898989">
                    </path>
                </svg>
            `

        return html`
            <svg width="${this.size}" height="${this.size}" viewBox="${this.viewBox}" fill="#898989" xmlns="http://www.w3.org/2000/svg">
                <path id="playback-toggle-1" d="M12 3C6.5 3 2 7.7 2 13.4V18C2 20.2 3.8 22 6 22C6.7 22 7.3 21.8 7.8 21.5C7.9 21.5 7.9 21.4 8 21.4C8.3 21.2 8.5 20.9 8.5 20.5V15.3C8.5 14.9 8.3 14.6 8 14.5C7.9 14.5 7.9 14.4 7.8 14.4C7.3 14.2 6.6 14 6 14C5.3 14 4.6 14.2 4 14.6V13.4C4 8.8 7.6 5 12 5C16.4 5 20 8.8 20 13.4V14.6C19.4 14.3 18.7 14 18 14C17.4 14 16.7 14.2 16.2 14.4C16.1 14.4 16.1 14.5 16 14.5C15.7 14.7 15.5 15 15.5 15.3V20.5C15.5 20.9 15.7 21.2 16 21.4C16.1 21.4 16.1 21.5 16.2 21.5C16.7 21.8 17.4 22 18 22C20.2 22 22 20.2 22 18V13.4C22 7.7 17.5 3 12 3ZM6 16C6.2 16 6.3 16 6.4 16.1V20C6.3 20 6.2 20 6 20C4.9 20 4 19.1 4 18C4 16.9 4.9 16 6 16ZM18 20C17.8 20 17.7 20 17.6 19.9V16C17.7 16 17.9 15.9 18 15.9C19.1 15.9 20 16.8 20 17.9C20 19 19.1 20 18 20Z" fill="#898989">
                    <animateTransform
                        id="ring"
                        href="#playback-toggle-1"
                        attributeName="transform"
                        type="rotate"
                        attributeType="XML"
                        begin="0s;ring.end+30s"
                        dur="1.8s"
                        repeatCount="1"
                        values="0 10 0; 10 14 10; 0 10 0; -10 14 10; 0 10 0;
                                0 10 0; 10 14 10; 0 10 0; -10 14 10; 0 10 0;
                                0 10 0; 10 14 10; 0 10 0; -10 14 10; 0 10 0;
                                0 10 0; 10 14 10; 0 10 0; -10 14 10; 0 10 0;
                                0 10 0; 10 14 10; 0 10 0; -10 14 10; 0 10 0;
                                0 10 0; 10 14 10; 0 10 0; -10 14 10; 0 10 0;"
                    />
                    <animateMotion
                        id="up"
                        href="#playback-toggle-1"
                        begin="0s;up.end+30s"
                        dur="1.8s"
                        repeatCount="1"
                        values="0 0 0; 0 0 0; 0 0 0;
                                0 0 0; 0 0 0; 0 0 0;
                                0 0 0; 0 0 0; 0 0 0;
                                0 0 0; 0 -2 0; 0 0 0;
                                0 0 0; 0 -2 0; 0 0 0;
                                0 0 0; 0 -2 0; 0 0 0;"
                    />
                    <animate
                        id="color"
                        href="#playback-toggle-1"
                        attributeName="fill"
                        begin="0s;color.end+30s"
                        dur="1.8s"
                        repeatCount="1"
                        values="
                            #898989FF;
                            #6785A7FF;
                            #4582C4FF;
                            #227EE2FF;
                            #007AFF;
                            #007AFF;
                            #227EE2FF;
                            #4582C4FF;
                            #6785A7FF;
                            #898989FF;"
                    />
                </path>
            </svg>
        `;
    }
}
