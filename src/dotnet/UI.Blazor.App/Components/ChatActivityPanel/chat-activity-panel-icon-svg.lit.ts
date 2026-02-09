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
            <div class="equalizer ${this.isActive ? "active" : ""}" style="width: ${this.size * 4}px; height: ${this.size * 4}px;">
                <div class="bar bar-1"></div>
                <div class="bar bar-2"></div>
                <div class="bar bar-3"></div>
            </div>
        `;
    }
}
