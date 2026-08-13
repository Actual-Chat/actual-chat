// The app's container:has(descendant) relationships, registered as presence classes - see
// mutation-processor.ts for why. Every class registered here is written by MutationProcessor
// and must never be set in markup.
//
// Only container subjects belong here. A :has() whose subject is a small element (.toggle,
// .checkbox, .navbar-item) walks a tiny subtree and costs nothing worth replacing.

import { MutationProcessor } from 'mutation-processor';

MutationProcessor.registerPresenceClasses(
    { container: '.chat-message-editor', match: '.related-chat-entry-panel', className: 'has-related-entry' },
    { container: '.list-view-layout', match: '.chat-activity-panel.listening', className: 'has-listening-activity' },
    { container: '.list-view-layout', match: '.audio-panel-header', className: 'has-audio-panel-header' },
    { container: '.right-panel', match: '.c-header.expanded-header', className: 'has-expanded-header' },
    { container: '.layout-header', match: '.header-activity-panel-wrapper', className: 'has-activity-panel' },
);
