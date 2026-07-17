import { customElement } from 'lit/decorators.js';
import { css, html, LitElement } from 'lit';

// The circle + tail of the map marker (Figma shape), content-agnostic: slot in an
// avatar or <map-marker-pin>. The tail's top edge is tucked under the circle,
// so the bubble has no seam.
@customElement('map-marker-bubble')
export class MapMarkerBubble extends LitElement {
    static styles = css`
        :host {
            position: relative;
            display: block;
            width: 32px;
            height: 39px;
        }
        svg {
            display: block;
            width: 100%;
            height: 100%;
        }
        .c-content {
            position: absolute;
            top: 2px;
            left: 2px;
            display: flex;
            align-items: center;
            justify-content: center;
            width: 28px;
            height: 28px;
        }
    `;

    protected render(): unknown {
        return html`
            <svg viewBox="0 0 32 39" fill="none" xmlns="http://www.w3.org/2000/svg">
                <path d="M16.09 38.64L10.45 31.3L21.55 31.54L16.09 38.64Z" fill="white"/>
                <circle cx="16" cy="16" r="15" fill="#E8E8E8" stroke="white" stroke-width="2"/>
            </svg>
            <div class="c-content"><slot></slot></div>
        `;
    }
}
