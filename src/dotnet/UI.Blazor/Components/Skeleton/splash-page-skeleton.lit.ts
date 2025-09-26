import { customElement, property } from 'lit/decorators.js';
import { css, html, LitElement } from 'lit';
import { messageStyles } from './styles.lit';

@customElement('splash-page-skeleton')
class SplashPageSkeleton extends LitElement {
    protected createRenderRoot() {
        return this;
    }

    static styles = [
        messageStyles, css`
            :host {
                width: 100%;
                height: 100%;
                scrollbar-width: none;
                display: flex;
                flex-direction: row;
            }

            :host::-webkit-scrollbar {
                display: none;
            }

            :host(.animated-skeleton) {
                animation: pulse 2s infinite;
            }
            @media (min-width: 1280px) {
                :host(.chat-view-skeleton-list) {
                    align-self: center;
                    max-width: 48rem;
                }
            }
            .c-line,
            ::part(splash-c-line) {
                display: flex;
                flex: 1 1 0;
                align-items: center;
                justify-content: center;
                height: 0.75rem;
                background: var(--skeleton);
                border-radius: 9999px;
                animation: pulse 2s infinite;
            }
        `];
    @property({type: String})
    isRightPanelVisible: 'true' | 'false' = 'false';

    protected render(): unknown {
        const cls = this.isRightPanelVisible === 'true' ? 'rp-open' : 'rp-closed';
        return html`
            <div part="splash-page" class="splash-page ${cls}">
                <div part="splash-l" class="skeleton-left-panel">
                    <div part="splash-l-thin" class="thin-left-skeleton-panel">
                        <thin-left-panel-skeleton count="2"/>
                    </div>
                    <div part="splash-l-wide" class="wide-left-skeleton-panel">
                        <div part="splash-l-wide-header" class="skeleton-panel-header">
                            <div part="splash-l-wide-header-title" class="c-title">
                                <string-skeleton firstWidth="3" secondWidth="3" maxWidth="12" style="max-width: 3rem;"></string-skeleton>
                                <span class="w-4"></span>
                                <string-skeleton firstWidth="10" secondWidth="10" height="10" rounded="true"></string-skeleton>
                            </div>
                        </div>
                        <div part="splash-l-wide-content" class="skeleton-panel-body">
                            <tab-skeleton></tab-skeleton>
                            <chat-list-skeleton count="20"></chat-list-skeleton>
                        </div>
                    </div>
                </div>
                <div part="splash-m" class="chat-skeleton-panel">
                    <div part="splash-m-header" class="skeleton-chat-header">
                        <div class="c-wrapper">
                            <div part="splash-m-header-icon" class="c-icon">
                                <round-skeleton radius="10" />
                            </div>
                            <div part="splash-m-header-title" class="c-title">
                                <string-skeleton firstWidth="3" secondWidth="8"/>
                            </div>
                        </div>
                    </div>
                    <div part="splash-m-content" class="skeleton-panel-body">
                        <chat-view-skeleton count="2"></chat-view-skeleton>
                        <string-skeleton firstWidth="2" secondWidth="4" height="6" system="true"></string-skeleton>
                        <chat-view-skeleton count="2"></chat-view-skeleton>
                        <string-skeleton firstWidth="2" secondWidth="4" height="6" system="true"></string-skeleton>
                        <chat-view-skeleton count="2"></chat-view-skeleton>
                        <string-skeleton firstWidth="2" secondWidth="4" height="6" system="true"></string-skeleton>
                        <chat-view-skeleton count="2"></chat-view-skeleton>
                    </div>
                    <div part="splash-m-footer" class="skeleton-panel-footer">
                        <chat-view-footer-skeleton />
                    </div>
                </div>
                <div part="splash-r" class="right-skeleton-panel">
                    <div part="splash-r-header" class="skeleton-panel-header"></div>
                    <div part="splash-r-content" class="skeleton-panel-body">
                        <div class="c-buttons">
                            <round-skeleton radius="16" rootCls="right-skeleton-panel"></round-skeleton>
                            <div class="c-right">
                                <round-skeleton rootCls="right-skeleton-panel"></round-skeleton>
                                <round-skeleton rootCls="right-skeleton-panel"></round-skeleton>
                                <round-skeleton rootCls="right-skeleton-panel"></round-skeleton>
                            </div>
                        </div>
                        <div class="c-description">
                            <string-skeleton firstWidth="4" secondWidth="4" rootCls="right-skeleton-panel"></string-skeleton>
                            <string-skeleton firstWidth="10" secondWidth="10" rootCls="right-skeleton-panel"></string-skeleton>
                            <string-skeleton firstWidth="10" secondWidth="10" rootCls="right-skeleton-panel"></string-skeleton>
                        </div>

                        <div class="c-notification">
                            <string-skeleton firstWidth="5" secondWidth="5" rootCls="right-skeleton-panel"/>
                        </div>

                        <div class="c-notification">
                            <string-skeleton firstWidth="4" secondWidth="4" rootCls="right-skeleton-panel"/>
                        </div>

                        <div class="c-tab">
                            <tab-skeleton />
                        </div>

                        <div class="members-container">
                            <div class="c-members">
                                <string-skeleton firstWidth="3" secondWidth="3" rootCls="right-skeleton-panel"></string-skeleton>
                                <chat-list-skeleton count="2" rootCls="page-with-header-and-footer"></chat-list-skeleton>
                            </div>
                            <div class="c-members">
                                <string-skeleton firstWidth="3" secondWidth="3" rootCls="right-skeleton-panel"></string-skeleton>
                                <chat-list-skeleton count="2" rootCls="page-with-header-and-footer"></chat-list-skeleton>
                            </div>
                            <div class="c-members">
                                <string-skeleton firstWidth="3" secondWidth="3" rootCls="right-skeleton-panel"></string-skeleton>
                                <chat-list-skeleton count="2" rootCls="page-with-header-and-footer"></chat-list-skeleton>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        `;
    }
}
