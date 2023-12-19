import {customElement} from "lit/decorators.js";
import {css, html, LitElement} from "lit";

import '../../../../nodejs/styles/index.css';

@customElement('chat-view-footer-skeleton')
class ChatViewFooterSkeleton extends LitElement {

    static styles = css`
    :host {
      display: flex;
      flex: 1 1 0%;
    }
    .footer-container {
        display: none;
    }
    .narrow-footer-container {
        display: flex;
        flex: 1 1 auto;
        flex-direction: column;
        column-gap: 0.625rem;
        align-items: center;
        background-color: var(--post-panel);
    }
    .panel-buttons {
        position: relative;
        top: -1px;
        display: flex;
        flex-direction: row;
        align-items: center;
        justify-content: center;
        column-gap: 1.5rem;
        height: 6rem;
        width: 100%;
        background-color: var(--background-01);
        border-bottom-left-radius: 2rem;
        border-bottom-right-radius: 2rem;
    }
    .panel-editor {
        min-height: 3.5rem;
    }
    @media (min-width: 820px) {
        .footer-container {
            display: flex;
            flex: 1 1 auto;
            flex-direction: row;
            column-gap: 0.625rem;
            align-items: center;
            margin: 0 0.75rem 0.25rem 0.75rem;
        }
        .narrow-footer-container {
            display: none;
        }
    }
    .footer-editor,
    .footer-button {
        height: 3rem;
        border-radius: 9999px;
        background-color: var(--background-03);
        animation: pulse 2s infinite;
    }
    .footer-button {
        flex: none;
        width: 3rem;
    }
    .panel-buttons > .footer-button.record {
        width: 5rem;
        height: 5rem;
    }
    .footer-container > .footer-button.record {
        width: 3.5rem;
        height: 3.5rem;
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

    protected render(): unknown {
        return html`
            <div class="footer-container">
                <div class="footer-editor"></div>
                <div class="footer-button"></div>
                <div class="footer-button record"></div>
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
}
