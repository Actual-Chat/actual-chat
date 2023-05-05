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
    :root {
        --card-left-padding: 0.25rem;
        --card-top-padding: 0.75rem;
        --chat-list-card-top-padding: 0.25rem;
        --card-height: 4rem;
        --chat-list-card-height: 3rem;
        --card-skeleton: linear-gradient(white var(--card-height), transparent 0);
        --chat-list-card-skeleton: linear-gradient(#F5F5F5 var(--chat-list-card-height), transparent 0);

        --avatar-size: 2.5rem;
        --avatar-position: var(--card-left-padding) var(--card-top-padding);
        --chat-list-avatar-position: var(--card-left-padding) var(--chat-list-card-top-padding);
        --avatar-skeleton: radial-gradient(circle 1.25rem at center, #E7E7E7 99%, transparent 0
        );

        --desc-line-1-height: 1rem;
        --desc-line-2-height: 0.75rem;
        --desc-line-3-height: 0.5rem;
        --desc-line-1-left-padding: 3.25rem;
        --desc-line-1-top-padding: 1rem;
        --chat-list-desc-line-1-top-padding: 0.5rem;
        --desc-line-2-left-padding: 3.25rem;
        --desc-line-2-top-padding: 2.5rem;
        --chat-list-desc-line-2-top-padding: 2rem;
        --desc-line-1-skeleton: linear-gradient(#E7E7E7 var(--desc-line-1-height), transparent 0);
        --desc-line-2-skeleton: linear-gradient(#E7E7E7 var(--desc-line-2-height), transparent 0);
        --desc-line-3-skeleton: linear-gradient(#E7E7E7 var(--desc-line-3-height), transparent 0);
        --desc-line-1-width: 20%;
        --desc-line-1-position: var(--desc-line-1-left-padding) var(--desc-line-1-top-padding);
        --chat-list-desc-line-1-position: var(--desc-line-1-left-padding) var(--chat-list-desc-line-1-top-padding);
        --desc-line-2-width:80%;
        --desc-line-2-position: var(--desc-line-2-left-padding) var(--desc-line-2-top-padding);
        --chat-list-desc-line-2-position: var(--desc-line-2-left-padding) var(--chat-list-desc-line-2-top-padding);

        --blur-width: 200px;
        --blur-size: var(--blur-width) calc(var(--card-height));

        --skeleton-wide-left-panel-width: 100%;
        --skeleton-thin-left-panel-width: auto;
        --skeleton-right-panel-width: 100%;
    }
    @media (min-width: 1024px) {
        :root {
            --skeleton-wide-left-panel-width: 20rem;
            --skeleton-thin-left-panel-width: 7.5rem;
            --skeleton-right-panel-width: 20rem;
        }
    }
    .card {
        flex: none;
        width: 100%;
        // height: var(--card-height);
        height: 4rem;
    }
    .chat-list.card {
        // height: var(--chat-list-card-height);
        height: 3rem;
    }

    .message-skeleton {
        display: flex;
        flex-direction: row;
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
        padding: 0.25rem 3rem 0.25rem 3rem;
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
    cls = "";

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

        if (this.cls != "chat-list") {
            return html`
                ${repeat(cards, (item) => item, (item, index) => html`
                             ${this.chatMessageTemplate()}
                         `)}
            `;
        } else {
            return html`
                ${repeat(cards, (item) => item, (item, index) => html`
                             ${this.chatListMessageTemplate()}
                         `)}
            `;
        }
    }
}
