import { customElement, property, state } from 'lit/decorators.js';
import { guard } from 'lit/directives/guard.js';
import { range } from 'lit/directives/range.js';
import { map } from 'lit/directives/map.js';
import { html, LitElement } from 'lit';
import { MessageWidth, randomIntFromInterval } from './helpers';
import { fastRaf } from 'fast-raf';

@customElement('chat-list-skeleton')
class ChatListSkeleton extends LitElement {
    protected createRenderRoot() {
        return this;
    }

    private observer: IntersectionObserver;

    @property()
    class = '';
    @property()
    messageCls = '';
    @property()
    count = 1;

    render() {
        const { count } = this;
        return html`
            ${guard([count], () => map(range(count), () => html`
                <div class='message-skeleton ${this.messageCls}'>
                    <div class='avatar-wrapper'>
                        <div class='avatar'></div>
                    </div>
                    <div class='c-container'>
                        <div class='title message ${this.getMessageWidth(2, 5)}'></div>
                        <div class='message ${this.getMessageWidth(4, 10)}'></div>
                    </div>
                </div>
            `))}
        `;
    }

    connectedCallback() {
        super.connectedCallback();

        this.observer = new IntersectionObserver(entries => {
            const isVisible = entries.some(e => e.isIntersecting);
            // console.warn('isVisible', isVisible, entries);
            fastRaf({
                write: () => this.classList.toggle('animated-skeleton', isVisible && this.count > 0)
            })
        });
        this.observer.observe(this);
    }

    disconnectedCallback() {
        super.disconnectedCallback();

        this.observer.disconnect();
    }

    private getMessageWidth(first: number, second: number): string {
        let num = randomIntFromInterval(first, second);
        return MessageWidth[num];
    }
}
