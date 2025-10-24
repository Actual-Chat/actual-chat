import { customElement, property } from 'lit/decorators.js';
import { html, LitElement } from 'lit';

@customElement('splash-page-skeleton')
class SplashPageSkeleton extends LitElement {
    protected createRenderRoot() {
        return this;
    }

    @property({type: String})
    isRightPanelVisible: 'true' | 'false' = 'false';

    protected render(): unknown {
        const rightPanelDataAttr = this.isRightPanelVisible === 'true' ? 'open' : 'closed';
        return html`
            <div class="page-with-header-and-footer">
<!--                Left Panel-->
                <div class="left-panel-skeleton side-nav side-nav-left">
                    <div class='left-panel'>
                        <div class="thin-left-panel-skeleton">
                            <thin-left-panel-skeleton count="2"/>
                        </div>
                        <div class="wide-left-panel-skeleton">
                            <div class="panel-header-skeleton">
                                <div class="c-title">
                                    <string-skeleton firstWidth="3" secondWidth="3" maxWidth="12" style="max-width: 3rem;"></string-skeleton>
                                    <span class="w-4"></span>
                                    <string-skeleton firstWidth="10" secondWidth="10" height="10" rounded="true"></string-skeleton>
                                </div>
                            </div>
                            <div class="panel-body-skeleton">
                                <tab-skeleton></tab-skeleton>
                                <chat-list-skeleton count="20"></chat-list-skeleton>
                            </div>
                        </div>
                    </div>
                </div>
<!--                Chat Panel-->
                <div class="chat-panel-skeleton">
                    <div class="chat-header-skeleton">
                        <div class="c-wrapper">
                            <div class="c-icon">
                                <round-skeleton radius="10" />
                            </div>
                            <div class="c-title">
                                <string-skeleton firstWidth="3" secondWidth="8"/>
                            </div>
                        </div>
                    </div>
                    <div class="panel-body-skeleton">
                        <chat-view-skeleton count="20"></chat-view-skeleton>
                    </div>
                    <div class="panel-footer-skeleton">
                        <chat-view-footer-skeleton />
                    </div>
                </div>
<!--                Right Panel    -->
                <div class="right-panel-skeleton side-nav side-nav-right" data-side-nav='${rightPanelDataAttr}'>
                    <div class="panel-header-skeleton"></div>
                    <div class="panel-body-skeleton">
                        <div class="c-buttons">
                            <round-skeleton radius="16" rootCls="right-panel-skeleton"></round-skeleton>
                            <div class="c-right">
                                <round-skeleton rootCls="right-panel-skeleton"></round-skeleton>
                                <round-skeleton rootCls="right-panel-skeleton"></round-skeleton>
                                <round-skeleton rootCls="right-panel-skeleton"></round-skeleton>
                            </div>
                        </div>
                        <div class="c-description">
                            <string-skeleton firstWidth="4" secondWidth="4" rootCls="right-panel-skeleton"></string-skeleton>
                            <string-skeleton firstWidth="10" secondWidth="10" rootCls="right-panel-skeleton"></string-skeleton>
                            <string-skeleton firstWidth="10" secondWidth="10" rootCls="right-panel-skeleton"></string-skeleton>
                        </div>

                        <div class="c-notification">
                            <string-skeleton firstWidth="5" secondWidth="5" rootCls="right-panel-skeleton"/>
                        </div>

                        <div class="c-notification">
                            <string-skeleton firstWidth="4" secondWidth="4" rootCls="right-panel-skeleton"/>
                        </div>

                        <div class="c-tab">
                            <tab-skeleton />
                        </div>

                        <div class="members-container">
                            <div class="c-members">
                                <string-skeleton firstWidth="3" secondWidth="3" rootCls="right-panel-skeleton"></string-skeleton>
                                <chat-list-skeleton count="2"></chat-list-skeleton>
                            </div>
                            <div class="c-members">
                                <string-skeleton firstWidth="3" secondWidth="3" rootCls="right-panel-skeleton"></string-skeleton>
                                <chat-list-skeleton count="2"></chat-list-skeleton>
                            </div>
                            <div class="c-members">
                                <string-skeleton firstWidth="3" secondWidth="3" rootCls="right-panel-skeleton"></string-skeleton>
                                <chat-list-skeleton count="2"></chat-list-skeleton>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        `;
    }
}
