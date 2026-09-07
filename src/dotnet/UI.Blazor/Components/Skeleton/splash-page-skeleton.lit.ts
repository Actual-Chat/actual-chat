import { customElement, property } from 'lit/decorators.js';
import { html, LitElement } from 'lit';

@customElement('splash-page-skeleton')
export class SplashPageSkeleton extends LitElement {
    protected createRenderRoot() {
        return this;
    }

    @property({ type: String })
        isRightPanelVisible: 'true' | 'false' = 'false';

    protected render(): unknown {
        // The app restores the stored right panel only on a wide screen - see RightPanel's constructor.
        // Rendering it as open in narrow mode would also shrink the left panel via the :has() rule in side-nav.css.
        const isRightPanelOpen = this.isRightPanelVisible === 'true'
            && !document.body.classList.contains('narrow');
        const rightPanelDataAttr = isRightPanelOpen ? 'open' : 'closed';
        return html`
            <div class="page-with-header-and-footer">
<!--                Left Panel -->
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
<!--                Chat Panel -->
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
<!--                Right Panel -->
                <div class="right-panel-skeleton side-nav side-nav-right" data-side-nav='${rightPanelDataAttr}'>
                    <chat-side-panel-skeleton></chat-side-panel-skeleton>
                </div>
            </div>
        `;
    }
}
