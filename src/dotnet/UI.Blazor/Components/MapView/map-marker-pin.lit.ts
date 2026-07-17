import { customElement } from 'lit/decorators.js';
import { css, html, LitElement } from 'lit';

// The complete no-avatar map marker from the Figma design, as one SVG: the ringed
// dot on the geo point (its center is 8px above the bottom edge), overlapped by the
// circle + tail bubble with the location pin inside. Path data is wrapped at command
// boundaries — newlines are valid separators there.
@customElement('map-marker-pin')
export class MapMarkerPin extends LitElement {
    static styles = css`
        :host {
            display: block;
            width: 2.25rem;
            height: 3.25rem;
        }
        svg {
            display: block;
            width: 100%;
            height: 100%;
        }
    `;

    protected render(): unknown {
        return html`
            <svg viewBox="0 0 36 52" fill="none" xmlns="http://www.w3.org/2000/svg">
                <circle cx="18" cy="43.5703" r="7" fill="#2970FF" stroke="white" stroke-width="2"/>
                <path d="M18.0928 40.6396L12.4531 35L23.5479 35.2412L18.0928 40.6396Z" fill="white"/>
                <rect x="1" y="1" width="34" height="34" rx="17" fill="#E8E8E8" stroke="white" stroke-width="2"/>
                <path fill-rule="evenodd" clip-rule="evenodd" d="
M25 14.9688C25 11.1028 21.866 7.96875 18 7.96875C14.134 7.96875 11 11.1028 11 14.9688
C11 17.2679 12.2565 19.0211 13.9717 20.8076C14.3973 21.251 14.8392 21.6855 15.291 22.1299
C15.7388 22.5703 16.1964 23.0206 16.6318 23.4795C17.1128 23.9863 17.5806 24.5201 18 25.0869
C18.4194 24.5201 18.8872 23.9863 19.3682 23.4795C19.8036 23.0206 20.2612 22.5703 20.709 22.1299
C21.1608 21.6855 21.6027 21.251 22.0283 20.8076C23.7435 19.0211 25 17.2679 25 14.9688ZM20 14.4688
C20 13.3642 19.1046 12.4688 18 12.4688C16.8954 12.4688 16 13.3642 16 14.4688
C16 15.5733 16.8954 16.4688 18 16.4688C19.1046 16.4688 20 15.5733 20 14.4688ZM27 14.9688
C27 18.0878 25.2564 20.3344 23.4717 22.1934C23.0223 22.6614 22.558 23.1164 22.1113 23.5557
C21.6608 23.9988 21.2275 24.4263 20.8193 24.8564C19.9987 25.7213 19.3285 26.548 18.8945 27.416
C18.7251 27.7548 18.3788 27.9688 18 27.9688C17.6212 27.9688 17.2749 27.7548 17.1055 27.416
C16.6715 26.548 16.0013 25.7213 15.1807 24.8564C14.7725 24.4263 14.3392 23.9988 13.8887 23.5557
C13.442 23.1164 12.9777 22.6614 12.5283 22.1934C10.7436 20.3344 9 18.0878 9 14.9688
C9 9.99819 13.0294 5.96875 18 5.96875C22.9706 5.96875 27 9.99819 27 14.9688ZM22 14.4688
C22 16.6779 20.2091 18.4688 18 18.4688C15.7909 18.4688 14 16.6779 14 14.4688
C14 12.2596 15.7909 10.4688 18 10.4688C20.2091 10.4688 22 12.2596 22 14.4688Z" fill="#2970FF"/>
            </svg>
        `;
    }
}
