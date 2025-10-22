import { customElement, property } from 'lit/decorators.js';
import { html, LitElement } from 'lit';

@customElement('chat-activity-panel-icon-svg')
class ChatActivityPanelIconSvg extends LitElement {
    protected createRenderRoot() {
        return this;
    }

    @property()
    size = 4;
    @property({ type: Boolean })
    isActive = false;

    protected render(): unknown {
        return html`
            <svg class="equalizer ${this.isActive ? " active" : ""}" width="${this.size * 4}" height="${this.size * 4}" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
                <path d="M12 6L12 17" stroke="var(--danger)" stroke-width="2" stroke-linecap="round" class="bar bar-1"/>
                <path d="M7 9V15" stroke="var(--danger)" stroke-width="2" stroke-linecap="round" class="bar bar-2"/>
                <path d="M17 9V15" stroke="var(--danger)" stroke-width="2" stroke-linecap="round" class="bar bar-3"/>
            </svg>
        `;
    }
}
