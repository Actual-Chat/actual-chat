// Presence names the app declares on elements no component renders. Everything else declares its
// own, in markup: a container carries data-children, a child carries data-child, and
// presence-tracker.ts writes data-has-{name} on the container while at least one child is present.
//
// body and html are the only such elements - the two host pages own their markup - and this runs at
// import time, so the declarations are in place before MutationProcessor.start() takes its first scan.

document.body.setAttribute('data-children', 'video-panel-shown video-panel-expanded audio-panel-header');
// The layouts whose bottom edge the iOS keyboard's corner notches reveal - see ios-keyboard-backdrop.css
document.documentElement.setAttribute('data-children', 'left-panel-open chat-footer modal');
