import { customElement } from 'lit/decorators.js';
import { css, html, LitElement } from 'lit';

// The circle + tail of the map marker (Figma shape) for markers with an avatar:
// same outline as <map-marker-pin>, with a slot in place of the pin. The tail's
// top edge is tucked under the circle, so the bubble has no seam.
@customElement('map-marker-bubble')
export class MapMarkerBubble extends LitElement {
    static styles = css`
        :host {
            position: relative;
            display: block;
            width: 2.25rem;
            height: 2.5625rem;
        }
        svg {
            display: block;
            width: 100%;
            height: 100%;
        }
        .c-content {
            position: absolute;
            top: 0.125rem;
            left: 0.125rem;
            display: flex;
            align-items: center;
            justify-content: center;
            width: 2rem;
            height: 2rem;
        }
    `;

    protected render(): unknown {
        return html`
            <svg viewBox="0 0 36 41" fill="none" xmlns="http://www.w3.org/2000/svg">
                <path d="M18.0928 40.6396L12.4531 35L23.5479 35.2412L18.0928 40.6396Z" fill="white"/>
                <rect x="1" y="1" width="34" height="34" rx="17" fill="#E8E8E8" stroke="white" stroke-width="2"/>
            </svg>
            <div class="c-content"><slot></slot></div>
        `;
    }
}
