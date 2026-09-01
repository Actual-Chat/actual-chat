// The app's container:has(descendant) relationships, registered as presence classes - see
// mutation-processor.ts for why. Every class registered here is written by MutationProcessor
// and must never be set in markup.
//
// Only container subjects belong here. A :has() whose subject is a small element (.toggle,
// .checkbox, .navbar-item) walks a tiny subtree and costs nothing worth replacing.

import { MutationProcessor } from 'mutation-processor';

const virtualListGroup = '.virtual-list .c-virtual-container .group';
const virtualListItem = '.virtual-list .c-virtual-container .item';

MutationProcessor.registerPresenceClasses(
    { container: '.chat-message-editor', match: '.related-chat-entry-panel', className: 'has-related-entry' },
    { container: '.list-view-layout', match: '.chat-activity-panel.listening', className: 'has-listening-activity' },
    { container: '.list-view-layout', match: '.audio-panel-header', className: 'has-audio-panel-header' },
    { container: '.right-panel', match: '.c-header.expanded-header', className: 'has-expanded-header' },
    { container: '.layout-header', match: '.header-activity-panel-wrapper', className: 'has-activity-panel' },
    // A page that carries its own heading, so the layout's header would be an empty bar with a
    // separator under it - see .has-headerless-page in main.css.
    { container: '.list-view-layout', match: '.headerless-page', className: 'has-headerless-page' },

    // The chat view's live-conversation block. Measured on an iPhone 13 Pro during a
    // fullscreen video call: 69 `.group` and 44 `.item` subjects, every predicate false,
    // and `matchHasPseudoClass` 20% of attributable main-thread time - the whole cost was
    // proving absence over and over.
    { container: virtualListGroup, match: ':scope > .item.expanded > .live-conversation-header', className: 'has-live-block' },
    { container: virtualListGroup, match: ':scope > .item.expanded > .live-conversation-header.unjoined', className: 'has-unjoined-live-block' },
    { container: virtualListGroup, match: ':scope > .item.expanded > .live-conversation-header.dissolving', className: 'has-live-block-dissolving' },
    { container: virtualListGroup, match: ':scope > .item > .conversation-message.live.joined', className: 'has-joined-live-card' },
    { container: virtualListItem, match: ':scope > .conversation-message', className: 'has-conversation-message' },
    { container: virtualListItem, match: ':scope > .conversation-message.live', className: 'has-live-card' },
    { container: virtualListItem, match: ':scope > .conversation-message.live:empty', className: 'has-empty-live-card' },
    { container: virtualListItem, match: ':scope > .live-conversation-footer', className: 'has-live-footer' },
    { container: virtualListItem, match: '.live-conversation-header', className: 'has-live-header' },
    { container: virtualListItem, match: '.show-author', className: 'has-show-author' },
    { container: virtualListItem, match: '.conversation-header', className: 'has-conversation-header' },
    // Either header makes the item itself position:sticky, which is what the virtual list has to know:
    // while its rubber band hides scroll from the content, sticky re-clamps against the real scroll
    // position and the item would otherwise drift away from everything around it. One rule, not two -
    // toggle(className, force) is absolute, so a second rule writing the same class would switch off
    // what the first had set whenever its own match was absent.
    { container: virtualListItem, match: '.conversation-header, .live-conversation-header', className: 'vl-sticky' },

    // The video panel's state, mirrored onto body. `body` is an ancestor of every mutation,
    // so a `body:has()` is re-proven on all of them - and the `.has-video-panel` compound
    // that guards these short-circuits everywhere EXCEPT during a call, which is the one
    // time it matters.
    { container: 'body', match: '.video-panel:not(.panel-hidden)', className: 'video-panel-shown' },
    { container: 'body', match: '.video-panel.expanded:not(.panel-hidden)', className: 'video-panel-expanded' },
    { container: 'body', match: '.video-panel.expanded', className: 'video-panel-expanded-any' },
    { container: '.layout-header', match: '.video-panel:not(.collapsed)', className: 'has-open-video-panel' },
    { container: '.layout-header', match: '.video-panel:not(.collapsed):not(.panel-hidden)', className: 'has-visible-video-panel' },
    { container: '.list-view-layout', match: '.video-panel:not(.collapsed)', className: 'has-open-video-panel' },
    // Shares a declaration block with the video-panel rules above, so it has to move with
    // them - left as :has() it would keep the specificity those rules just gave up.
    { container: '.list-view-layout', match: '.map-panel', className: 'has-map-panel' },
    { container: '.list-view-layout .layout-header', match: '.map-panel', className: 'has-map-panel' },
    { container: '.video-panel', match: '.video-panel-chat', className: 'has-video-panel-chat' },
    { container: '.video-panel', match: '.video-panel-chat:not(.chat-hidden)', className: 'has-video-panel-chat-shown' },

    { container: '.tab-panel-tabs .tab-btn .btn-content', match: '.c-badge', className: 'has-badge' },
    { container: '.chat-message-markup', match: '.code-block-markup', className: 'has-code-block' },
    { container: '.chat-list .navbar-item .navbar-item-ending', match: '.chat-audio-controls', className: 'has-audio-controls' },
    { container: '.chat-list .navbar-item-content .c-last-message', match: '.c-text.two-line', className: 'has-two-line-text' },
    { container: '.chat-list .navbar-item-content .c-last-message .c-name', match: '.non-shrinkable', className: 'has-non-shrinkable' },
    { container: '.chat-list .navbar-item-content .c-last-message .c-name', match: '.shrinkable', className: 'has-shrinkable' },
    { container: '.found-result.navbar-item .c-description', match: '.c-second-line', className: 'has-second-line' },
    { container: '.found-result.navbar-item .c-description', match: '.c-third-line', className: 'has-third-line' },
    { container: '.found-result.navbar-item .c-description', match: '.c-name .non-shrinkable', className: 'has-non-shrinkable-name' },
);
