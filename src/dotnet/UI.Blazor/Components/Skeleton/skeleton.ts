import {LitElement, html, css} from 'lit';
import {customElement, state, query, property} from 'lit/decorators.js';
import {repeat} from 'lit/directives/repeat.js';

class Card {
    titleWordCount: number;
    stringCount: number;
    constructor(t: number, s: number) {
        this.titleWordCount = t;
        this.stringCount = s;
    }
}
enum MessageWidth {
    "w-1" = 1,
    "w-2" = 2,
    "w-3" = 3,
    "w-4" = 4,
    "w-5" = 5,
    "w-6" = 6,
    "w-7" = 7,
    "w-8" = 8,
    "w-9" = 9,
    "w-10" = 10,
}

@customElement('chat-skeleton')
class ChatSkeleton extends LitElement {

    static styles = css`
    .message-skeleton {
        display: flex;
        flex-direction: row;
        column-gap: 0.25rem;
        animation: pulse 2s infinite;
    }
    .avatar-wrapper {
        display: flex;
        flex: none;
        align-items: center;
        justify-content: center;
        width: 3rem;
        height: 3rem;
    }
    .avatar {
        width: 2.5rem;
        height: 2.5rem;
        border-radius: 9999px;
        background: #e7e7e7;
    }
    .message-skeleton .c-container {
        display: flex;
        flex-direction: column;
        align-items: start;
        justify-content: center;
        row-gap: 0.25rem;
        width: 100%;
    }
    .message-skeleton .title.message {
        height: 1rem;
        background: #edf0f3;
        border-radius: 0.375rem;
    }
    .message-skeleton .message {
        height: 1rem;
        background: #edf0f3;
        border-radius: 0.375rem;
    }
    .chat-list .message-list {
        display: none;
    }
    .message-list {
        display: flex;
        flex-direction: column;
        column-gap: 0.25rem;
        margin-bottom: 0.5rem;
        animation: pulse 2s infinite;
    }
    .message-wrapper {
        display: flex;
        flex-direction: flex-row;
        flex-wrap: wrap;
        align-items: center;
        row-gap: 0.5rem;
        padding: 0.25rem 3.25rem 0.25rem 3.25rem;
    }
    .message {
        display: flex;
        height: 1rem;
        background: #edf0f3;
        border-radius: 0.375rem;
    }
    .message.w-1 {
        width: 10%;
    }
    .message.w-2 {
        width: 20%;
    }
    .message.w-3 {
        width: 30%;
    }
    .message.w-4 {
        width: 40%;
    }
    .message.w-5 {
        width: 50%;
    }
    .message.w-6 {
        width: 60%;
    }
    .message.w-7 {
        width: 70%;
    }
    .message.w-8 {
        width: 80%;
    }
    .message.w-9 {
        width: 90%;
    }
    .message.w-10 {
        width: 100%;
    }
    .thin-left-panel-button {
        width: 2.5rem;
        height: 2.5rem;
        margin-bottom: 0.5rem;
        border-radius: 0.75rem;
        background: #F5F5F5;
        animation: pulse 2s infinite;
    }
    .thin-left-panel-button.footer-button {
        width: 2.5rem;
        height: 2.5rem;
        margin-bottom: 0.5rem;
        background: #F5F5F5;
    }
    .footer-container {
        display: none;
    }
    .narrow-footer-container {
        display: flex;
        flex-direction: column;
        column-gap: 0.625rem;
        align-items: center;
    }
    .panel-buttons {
        display: flex;
        flex-direction: row;
        align-items: center;
        justify-content: center;
        column-gap: 1.5rem;
        height: 6rem;
        width: 100%;
        background: #ffffff;
        border-bottom-left-radius: 2rem;
        border-bottom-right-radius: 2rem;
    }
    .panel-editor {
        min-height: 3.5rem;
    }
    @media (min-width: 1024px) {
        .thin-left-panel-button {
            width: 3rem;
            height: 3rem;
            border-radius: 0.5rem;
        }
        .thin-left-panel-button.footer-button {
            width: 3rem;
            height: 3rem;
        }
        .footer-container {
            display: flex;
            flex-direction: row;
            column-gap: 0.625rem;
            align-items: center;
        }
        .narrow-footer-container {
            display: none;
        }
    }
    .chat-view-footer {
        flex: 1 1 0%;
    }
    .footer-editor,
    .footer-button {
        height: 3rem;
        border-radius: 9999px;
        background: #f5f5f5;
        animation: pulse 2s infinite;
    }
    .footer-button {
        flex: none;
        width: 3rem;
    }
    .footer-button.record {
        width: 5rem;
        height: 5rem;
    }
    .footer-editor {
        flex: 1 1 0%;
    }

    @keyframes pulse {
      0%, 100% {
        opacity: 1;
      }
      50% {
        opacity: .5;
      }
    }
  `;

    @property()
    class = "";

    @property()
    count = 1;

    private getMessageWidth(first: number, second: number) : string {
        let num = this.randomIntFromInterval(first, second);
        return MessageWidth[num];
    }

    chatMessageTemplate() {
        const messages = [];
        const messageCount = this.randomIntFromInterval(0,4);
        for (let i = 0; i < messageCount; i++) {
            messages.push(html`<div class="message-wrapper">
                <div class="message ${this.getMessageWidth(4, 10)}"></div>
            </div>`);
        }
        return html`
            <div class="message-skeleton">
                <div class="avatar-wrapper">
                    <div class="avatar"></div>
                </div>
                <div class="c-container">
                    <div class="title message ${this.getMessageWidth(2, 5)}"></div>
                    <div class="message ${this.getMessageWidth(4, 10)}"></div>
                </div>
            </div>
            <div class="message-list">
                ${messages}
            </div>
        `;
    }

    chatListMessageTemplate() {
        return html`
            <div class="message-skeleton">
                <div class="avatar-wrapper">
                    <div class="avatar"></div>
                </div>
                <div class="c-container">
                    <div class="title message ${this.getMessageWidth(2, 5)}"></div>
                    <div class="message ${this.getMessageWidth(4, 10)}"></div>
                </div>
            </div>
        `;
    }

    thinLeftPanelTemplate() {
        return html`
            <div class="thin-left-panel-button">
            </div>
        `;
    }

    chatViewFooterTemplate() {
        return html`
            <div class="footer-container">
                <div class="footer-editor"></div>
                <div class="footer-button"></div>
                <div class="footer-button"></div>
                <div class="footer-button"></div>
            </div>
            <div class="narrow-footer-container">
                <div class="panel-buttons">
                    <div class="footer-button"></div>
                    <div class="footer-button record"></div>
                    <div class="footer-button"></div>
                </div>
                <div class="panel-editor">

                </div>
            </div>
        `;
    }
    thinLeftPanelFooterTemplate() {
        return html`
            <div class="footer-container">
                <div class="footer-button thin-left-panel-button"></div>
            </div>
        `;
    }

    private randomIntFromInterval(min, max) : number {
        return Math.floor(Math.random() * (max - min + 1) + min);
    }

    private cards = Array<Card>();

    private getCards = () : Array<Card> => {
        for(let i = 0; i < this.count; i++) {
            let card = new Card(1, 2);
            this.cards.push(card);
        }
        return this.cards;
    }


    render() {
        let cards = this.getCards();

        if (this.class == "chat-view") {
            return html`
                ${repeat(cards, (item) => item, (item, index) => html`
                     ${this.chatMessageTemplate()}
                `)}
            `;
        } else if (this.class == "chat-list") {
            return html`
                ${repeat(cards, (item) => item, (item, index) => html`
                    ${this.chatListMessageTemplate()}
                `)}
            `;
        } else if (this.class == "thin-left-panel") {
            return html`
                ${repeat(cards, (item) => item, (item, index) => html`
                    ${this.thinLeftPanelTemplate()}
                `)}
            `;
        } else if (this.class == "chat-view-footer") {
            return html`
                ${this.chatViewFooterTemplate()}
            `;
        } else if (this.class == "thin-left-panel-footer") {
            return html`
                ${this.thinLeftPanelFooterTemplate()}
            `;
        } else {
            return ;
        }
    }
}
