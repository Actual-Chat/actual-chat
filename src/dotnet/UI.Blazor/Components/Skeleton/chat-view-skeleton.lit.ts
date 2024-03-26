import {customElement, property} from "lit/decorators.js";
import {css, html, LitElement} from "lit";
import {messageStyles} from "./styles.lit";

@customElement('chat-view-skeleton')
class ChatViewSkeleton extends LitElement {

    static styles = [messageStyles, css`
        :host {
            width: 100%;
            margin: 0.375rem;
            scrollbar-width: none;
        }
        :host::-webkit-scrollbar {
            display: none;
        }
    `];

    @property()
    count = 2;

    protected render(): unknown {
        return html`
            ${[...new Array(Number(this.count))].map(() => html`
                <chat-message-skeleton/>
            `)}
        `;
    }
}
