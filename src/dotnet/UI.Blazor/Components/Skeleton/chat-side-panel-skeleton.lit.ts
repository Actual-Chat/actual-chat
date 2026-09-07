import { customElement } from 'lit/decorators.js';
import { html, LitElement } from 'lit';

/** Placeholder for ChatSidePanel: its header geometry is mirrored in skeleton.css, so the real
 *  panel replaces it without moving the cover, avatar, title or tabs. */
@customElement('chat-side-panel-skeleton')
export class ChatSidePanelSkeleton extends LitElement {
    protected createRenderRoot() {
        return this;
    }

    protected render(): unknown {
        return html`
            <div class="c-header">
                <div class="c-cover"></div>
                <div class="c-avatar">
                    <round-skeleton radius="16"></round-skeleton>
                </div>
                <div class="c-bottom">
                    <string-skeleton firstWidth="5" secondWidth="5" maxWidth="30"></string-skeleton>
                    <div class="c-badge"></div>
                </div>
            </div>
            <div class="c-chat-info"></div>
            <div class="c-tabs">
                <tab-skeleton></tab-skeleton>
            </div>
            <div class="c-divider"></div>
            <div class="c-list">
                <chat-list-skeleton count="8"></chat-list-skeleton>
            </div>
        `;
    }
}
