// TODO: Fix ESLint errors
/* eslint-disable @typescript-eslint/no-unused-vars */
import { customElement, property } from 'lit/decorators.js';
import { html, svg, LitElement } from 'lit';
import { AnimationSync } from 'animation-sync';

@customElement('chat-activity-panel-icon-svg')
class ChatActivityPanelIconSvg extends LitElement {
    protected createRenderRoot() {
        return this;
    }

    @property() size = 4;
    @property({ type: Boolean }) isActive = false;
    @property() mode: 'audio' | 'video' = 'audio';

    // Animated nodes live in the shadow root, where a document sweep cannot
    // reach them, and they are new elements on every re-render.
    protected updated(): void {
        AnimationSync.syncAll(this.renderRoot);
    }

    protected render(): unknown {
        if (this.mode === 'video') {
            this.size = 6;
            return html`
                <svg class="video-icon ${this.isActive ? ' active' : ''}" width="${this.size * 4}" height="${this.size * 4}" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
                    <defs>
                        <linearGradient id="video-live-grad" x1="0" y1="0" x2="0" y2="1">
                            <stop offset="0%" stop-color="var(--violet-70)"/>
                            <stop offset="100%" stop-color="var(--indigo-70)"/>
                        </linearGradient>
                    </defs>
                    <rect class="video-frame" data-anim-sync x="2" y="3" width="20" height="16" rx="3.2" ry="3.2"
                          pathLength="100"
                          stroke="url(#video-live-grad)" stroke-width="2"
                          stroke-linecap="round" stroke-linejoin="round"
                          stroke-dasharray="90 10"/>
                    ${this.isActive ? svg`
                        <rect class="eq-bar b1" data-anim-sync x="7.5"  y="8" width="2" height="7" rx="1" fill="url(#video-live-grad)"/>
                        <rect class="eq-bar b2" data-anim-sync x="11"   y="8" width="2" height="7" rx="1" fill="url(#video-live-grad)"/>
                        <rect class="eq-bar b3" data-anim-sync x="14.5" y="8" width="2" height="7" rx="1" fill="url(#video-live-grad)"/>
                    ` : svg`
                        <path class="play-arrow" data-anim-sync
                              d="M9 15V7L17 11.3077L9 15Z"
                              fill="none"
                              stroke="url(#video-live-grad)" stroke-width="2"
                              stroke-linecap="round" stroke-linejoin="round"/>
                    `}
                    <style>
                        @keyframes frame-gap {
                            from { stroke-dashoffset: 0; }
                            to   { stroke-dashoffset: 100; }
                        }
                        @keyframes play-pulse {
                            0%, 100% { opacity: 0.6; }
                            50%      { opacity: 1; }
                        }
                        @keyframes eq {
                            0%, 100% { transform: scaleY(0.35); }
                            50%      { transform: scaleY(1); }
                        }
                        .video-frame {
                            animation: frame-gap 4s steps(40) infinite;
                        }
                        .play-arrow {
                            animation: play-pulse 2.4s steps(24) infinite;
                        }
                        .eq-bar {
                            transform-origin: center;
                            transform-box: fill-box;
                        }
                        /* A stagger shifts every tick with it, so it has to be a whole
                           number of 100ms grid steps - 0.15s puts this bar 50ms off. */
                        .b1 { animation: eq 0.9s steps(9) infinite; }
                        .b2 { animation: eq 0.9s steps(9) infinite 0.1s; }
                        .b3 { animation: eq 0.9s steps(9) infinite 0.2s; }
                    </style>
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
