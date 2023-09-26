import {customElement, property} from "lit/decorators.js";
import {css, html, LitElement} from "lit";

@customElement('playback-toggle-svg')
class PlaybackToggleSvg extends LitElement {
    @property()
    size = "32";
    @property()
    viewBox = "-3 -4 30 30";
    @property({ type: Boolean })
    isAnimated = false;

    protected render(): unknown {
        if (!this.isAnimated)
            return html`
            <svg width="${this.size}" height="${this.size}" viewBox="${this.viewBox}" fill="none" xmlns="http://www.w3.org/2000/svg">
                <path id="listen-svg-arrow" fill-rule="evenodd" clip-rule="evenodd" d="M12.707 12.2923C12.322 11.9063 11.699 11.9033 11.305 12.2793L8.30502 15.1763C7.90802 15.5603 7.89702 16.1923 8.28102 16.5903C8.66502 16.9873 9.29802 17.0003 9.69502 16.6153L11 15.3553V20.9993C11 21.5523 11.448 21.9993 12 21.9993C12.552 21.9993 13 21.5523 13 20.9993V15.4133L14.293 16.7063C14.488 16.9013 14.744 16.9993 15 16.9993C15.256 16.9993 15.512 16.9013 15.707 16.7063C16.098 16.3153 16.098 15.6833 15.707 15.2923L12.707 12.2923Z" fill="#007AFF">
                </path>
                <path id="listen-svg-1" d="M5 12C5 7.13401 8.13401 4 12 4C15.866 4 19 7.13401 19 12" stroke="#898989" stroke-width="2">
                </path>
                <path id="listen-svg-2" d="M2 15.5C2 17.433 3.34315 19 5 19L5 12C3.34315 12 2 13.567 2 15.5Z" stroke="#898989" stroke-width="2" stroke-linejoin="round">
                </path>
                <path id="listen-svg-3" d="M22 15.5C22 13.567 20.6569 12 19 12L19 19C20.6569 19 22 17.433 22 15.5Z" stroke="#898989" stroke-width="2" stroke-linejoin="round">
                </path>
            </svg>`

        return html`
            <svg width="${this.size}" height="${this.size}" viewBox="${this.viewBox}" fill="none" xmlns="http://www.w3.org/2000/svg">
                <path id="listen-svg-arrow" fill-rule="evenodd" clip-rule="evenodd" d="M12.707 12.2923C12.322 11.9063 11.699 11.9033 11.305 12.2793L8.30502 15.1763C7.90802 15.5603 7.89702 16.1923 8.28102 16.5903C8.66502 16.9873 9.29802 17.0003 9.69502 16.6153L11 15.3553V20.9993C11 21.5523 11.448 21.9993 12 21.9993C12.552 21.9993 13 21.5523 13 20.9993V15.4133L14.293 16.7063C14.488 16.9013 14.744 16.9993 15 16.9993C15.256 16.9993 15.512 16.9013 15.707 16.7063C16.098 16.3153 16.098 15.6833 15.707 15.2923L12.707 12.2923Z" fill="#007AFF">
                    <animateMotion
                        href="#listen-svg-arrow"
                        begin="0s"
                        dur="0.75s"
                        repeatCount="2"
                        values="0 0 0; 0 -1 0; 0 0 0; 0 0 0; 0 0 0"
                    >
                    </animateMotion>
                </path>
                <path id="listen-svg-1" d="M5 12C5 7.13401 8.13401 4 12 4C15.866 4 19 7.13401 19 12" stroke="#898989" stroke-width="2">
                    <animate
                        href="#listen-svg-1"
                        attributeName="d"
                        attributeType="XML"
                        begin="0s"
                        dur="0.75s"
                        repeatCount="2"
                        values="
                        M5 12C5 7.13401 8.13401 4 12 4C15.866 4 19 7.13401 19 12;
                         M2 12C5 7.13401 8.13401 4 12 4C15.866 4 19 7.13401 22 12;
                         M3 12C5 7.13401 8.13401 4 12 4C15.866 4 19 7.13401 21 12;
                          M5 12C5 7.13401 8.13401 4 12 4C15.866 4 19 7.13401 19 12;
                          M5 12C5 7.13401 8.13401 4 12 4C15.866 4 19 7.13401 19 12;"
                    />
                    <animateMotion
                        href="#listen-svg-1"
                        begin="0s"
                        dur="0.75s"
                        repeatCount="2"
                        values="0 0 0; 0 -5 0; 0 -3 0; 0 0 0; 0 0 0"
                    >
                    </animateMotion>
                </path>
                <path id="listen-svg-2" d="M2 15.5C2 17.433 3.34315 19 5 19L5 12C3.34315 12 2 13.567 2 15.5Z" stroke="#898989" stroke-width="2" stroke-linejoin="round">
                    <animateTransform
                        href="#listen-svg-2"
                        attributeName="transform"
                        type="rotate"
                        attributeType="XML"
                        begin="0s"
                        dur="0.75s"
                        repeatCount="2"
                        values="
                        0 10 0;
                           10 14 0;
                           10 14 0;
                           0 10 0;
                             0 10 0"
                    />
                    <animateMotion
                        href="#listen-svg-2"
                        begin="0s"
                        dur="0.75s"
                        repeatCount="2"
                        values="0 0 0; 0 -5 0; 0 -3 0; 0 0 0; 0 0 0"
                    >
                    </animateMotion>
                </path>
                <path id="listen-svg-3" d="M22 15.5C22 13.567 20.6569 12 19 12L19 19C20.6569 19 22 17.433 22 15.5Z" stroke="#898989" stroke-width="2" stroke-linejoin="round">
                    <animateTransform
                        href="#listen-svg-3"
                        attributeName="transform"
                        type="rotate"
                        attributeType="XML"
                        begin="0s"
                        dur="0.75s"
                        repeatCount="2"
                        values="
                        0 10 0;
                           -10 14 0;
                           -10 14 0;
                           0 10 0;
                             0 10 0"
                    />
                    <animateMotion
                        href="#listen-svg-3"
                        begin="0s"
                        dur="0.75s"
                        repeatCount="2"
                        values="0 0 0; 0 -5 0; 0 -3 0; 0 0 0; 0 0 0"
                    >
                    </animateMotion>
                </path>
            </svg>
        `;
    }
}
