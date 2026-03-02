// TODO: Fix ESLint errors
/* eslint-disable @typescript-eslint/no-unused-vars */
import { customElement, property } from 'lit/decorators.js';
import { html, LitElement } from 'lit';

@customElement('chat-activity-panel-icon-svg')
class ChatActivityPanelIconSvg extends LitElement {
    protected createRenderRoot() {
        return this;
    }

    @property() size = 4;
    @property({ type: Boolean }) isActive = false;
    @property() mode: 'audio' | 'video' = 'audio';

    protected render(): unknown {
        if (this.mode === 'video') {
            return html`
                <svg class="video-icon ${this.isActive ? ' active' : ''}" width="${this.size * 4}" height="${this.size * 4}" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
                    <rect x="2" y="6" width="13" height="12" rx="2" stroke="var(--danger)" stroke-width="2" fill="none"/>
                    <path d="M15 10L21 7V17L15 14" stroke="var(--danger)" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" fill="none"/>
                </svg>
            `;
        }

        return html`
            <div class="equalizer ${this.isActive ? 'active' : ''}" style="width: ${this.size * 4}px; height: ${this.size * 4}px;">
                <div class="bar bar-1"></div>
                <div class="bar bar-2"></div>
                <div class="bar bar-3"></div>
            </div>
        `;
    }
}
