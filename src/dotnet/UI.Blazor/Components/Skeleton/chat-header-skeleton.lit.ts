import {customElement, property} from "lit/decorators.js";
import {html, LitElement} from "lit";
import {HTMLTemplateResult} from "lit-html/development/lit-html";
import {messageStyles} from "./styles.lit";

@customElement('chat-header-skeleton')
class ChatHeaderSkeletonLit extends LitElement {
    static styles = [messageStyles,
        ];

    render(): HTMLTemplateResult {
        return html`
            <div class="message-skeleton header-skeleton">
                <div class="message-avatar-wrapper">
                    <div class="message-avatar"></div>
                </div>
                <div class="c-container">
                    <div class="title message"></div>
                </div>
            </div>
        `;
    }
}
