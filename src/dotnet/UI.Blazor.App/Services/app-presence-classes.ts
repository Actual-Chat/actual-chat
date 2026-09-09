// Presence names the app declares on elements no component renders. Everything else declares its
// own, in markup: a container carries data-children, a child carries data-child, and
// presence-tracker.ts writes data-has-{name} on the container while at least one child is present.
//
// body is the only such element - the two host pages own its markup - and this runs at import time,
// so the declaration is in place before MutationProcessor.start() takes its first scan.

document.body.setAttribute('data-children', 'video-panel-shown video-panel-expanded');
