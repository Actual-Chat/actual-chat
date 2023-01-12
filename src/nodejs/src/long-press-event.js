/*!
 * long-press-event - v@version@
 * Pure JavaScript long-press-event
 * https://github.com/john-doherty/long-press-event
 * @author John Doherty <www.johndoherty.info>
 * @license MIT
 */
(function (window, document) {

    'use strict';

    const defaultDelay = '500'; // 500ms, must be string
    const ua = navigator.userAgent.toLowerCase();
    const isAndroid = ua.indexOf('android') > -1;
    const captureEventsOption = { capture: true };

    const state = {
        cancelNextClickAfterMouseUpEvent: false,
        cancelNextClickEvent: false,
    };
    window.longPressEvent = state;

    let timer = null;
    let startX = 0; // mouse x position when timer started
    let startY = 0; // mouse y position when timer started
    let maxDiffX = isAndroid ? 5 : 10; // max number of X pixels the mouse can move during long press before it is canceled
    let maxDiffY = isAndroid ? 5 : 10; // max number of Y pixels the mouse can move during long press before it is canceled

    // patch CustomEvent to allow constructor creation (IE/Chrome)
    if (typeof window.CustomEvent !== 'function') {
        window.CustomEvent = function (event, params) {
            params = params || { bubbles: false, cancelable: false, detail: undefined };
            let evt = document.createEvent('CustomEvent');
            evt.initCustomEvent(event, params.bubbles, params.cancelable, params.detail);
            return evt;
        };

        window.CustomEvent.prototype = window.Event.prototype;
    }

    let defaultRequestAnimationFrame = window.requestAnimationFrame
        || window.webkitRequestAnimationFrame
        || window.mozRequestAnimationFrame
        || window.oRequestAnimationFrame
        || window.msRequestAnimationFrame;
    let defaultCancelAnimationFrame = window.cancelAnimationFrame
        || window.webkitCancelAnimationFrame
        || window.webkitCancelRequestAnimationFrame
        || window.mozCancelRequestAnimationFrame
        || window.oCancelRequestAnimationFrame
        || window.msCancelRequestAnimationFrame;
    if (!(defaultCancelAnimationFrame && !defaultCancelAnimationFrame)) {
        defaultRequestAnimationFrame = null;
        defaultCancelAnimationFrame = null;
    }
    // console.log('RAF:', defaultRequestAnimationFrame, defaultCancelAnimationFrame);

    let requestAnimationFrame = function (callback) {
        if (defaultRequestAnimationFrame)
            return defaultRequestAnimationFrame(callback);
        else
            window.setTimeout(callback, 1000 / 60);
    };

    /**
     * Behaves the same as setTimeout except uses requestAnimationFrame() where possible for better performance
     * @param {function} fn The callback function
     * @param {int} delay The delay in milliseconds
     * @returns {object} handle to the timeout object
     */
    function requestTimeout(fn, delay) {
        if (!defaultRequestAnimationFrame)
            return {
                timeout: window.setTimeout(fn, delay),
            }

        const start = new Date().getTime();
        const handle = {
            value: null,
        };

        const loop = function () {
            const delta = new Date().getTime() - start;
            if (delta >= delay)
                fn.call();
            else
                handle.value = requestAnimationFrame(loop);
        };

        handle.value = requestAnimationFrame(loop);
        return handle;
    }

    function clearRequestTimeout(handle) {
        if (handle?.timeout)
            clearTimeout(handle.timeout);
        else if (handle?.value)
            defaultCancelAnimationFrame(handle.value);
    }

    /**
     * Fires the 'long-press' event on element
     * @param {MouseEvent|PointerEvent|TouchEvent} originalEvent The original event being fired
     * @returns {void}
     */
    function fireLongPressEvent(originalEvent) {
        clearLongPressTimer();

        originalEvent = unifyEvent(originalEvent);
        let event = new CustomEvent('long-press', {
            bubbles: true,
            cancelable: true,

            // custom event data (legacy)
            detail: {
                clientX: originalEvent.clientX,
                clientY: originalEvent.clientY
            },

            // add coordinate data that would typically acompany a touch/click event
            clientX: originalEvent.clientX,
            clientY: originalEvent.clientY,
            offsetX: originalEvent.offsetX,
            offsetY: originalEvent.offsetY,
            pageX: originalEvent.pageX,
            pageY: originalEvent.pageY,
            screenX: originalEvent.screenX,
            screenY: originalEvent.screenY
        });

        const mustHandleDefault = this.dispatchEvent(event);
        state.cancelNextClickEvent = false;
        state.cancelNextClickAfterMouseUpEvent = !mustHandleDefault || !event.cancelBubble;
    }

    /**
     * consolidates mouse, touch, and Pointer events
     * @param {MouseEvent|PointerEvent|TouchEvent} e The original event being fired
     * @returns {MouseEvent|PointerEvent|Touch}
     */
    function unifyEvent(e) {
        return e.changedTouches !== undefined ? e.changedTouches[0] : e;
    }

    /**
     * method responsible for starting the long press timer
     * @param {event} e - event object
     * @returns {void}
     */
    function startLongPressTimer(e) {
        clearLongPressTimer(e);

        const el = e.target;
        const delayText = getNearestAttributeValue(el, 'data-long-press-delay', defaultDelay);
        const delay = parseInt(delayText);
        timer = requestTimeout(fireLongPressEvent.bind(el, e), delay);
    }

    /**
     * method responsible for clearing a pending long press timer
     * @param {event} e - event object
     * @returns {void}
     */
    function clearLongPressTimer(e) {
        clearRequestTimeout(state.timer);
        state.timer = null;
    }

    /**
    * Cancels the current event
    * @param {object} e - browser event object
    * @returns {void}
    */
    function cancelEvent(e) {
        e.stopPropagation();
        e.stopImmediatePropagation();
        e.preventDefault();
    }

    /**
     * Starts the timer on mouse down and logs current position
     * @param {object} e - browser event object
     * @returns {void}
     */
    function mouseDownHandler(e) {
        state.cancelNextClickAfterMouseUpEvent = false;
        state.cancelNextClickEvent = false;

        // NOTE(AY): Only primary button should trigger long presses!
        if (e.button !== 0)
            return;

        startX = e.clientX;
        startY = e.clientY;
        startLongPressTimer(e);
    }

    function mouseUpHandler(e) {
        clearLongPressTimer();

        if (!state.cancelNextClickAfterMouseUpEvent)
            return;

        state.cancelNextClickAfterMouseUpEvent = false;
        state.cancelNextClickEvent = true;
        // Let's stop click cancellation anyway in 200ms
        requestTimeout(function() {
            state.cancelNextClickEvent = false;
        }, 200);
    }

    function clickHandler(e) {
        clearLongPressTimer();

        if (state.cancelNextClickEvent) {
            state.cancelNextClickEvent = false;
            cancelEvent(e);
            // console.log('click event is cancelled after long-press event');
        }
    }

    /**
     * If the mouse moves n pixels during long-press, cancel the timer
     * @param {object} e - browser event object
     * @returns {void}
     */
    function mouseMoveHandler(e) {
        // calculate total number of pixels the pointer has moved
        const diffX = Math.abs(startX - e.clientX);
        const diffY = Math.abs(startY - e.clientY);

        // if pointer has moved more than allowed, cancel the long-press timer and therefore the event
        if (diffX >= maxDiffX || diffY >= maxDiffY)
            clearLongPressTimer(e);
    }

    /**
     * Gets attribute off HTML element or nearest parent
     * @param {object} el - HTML element to retrieve attribute from
     * @param {string} attributeName - name of the attribute
     * @param {any} defaultValue - default value to return if no match found
     * @returns {any} attribute value or defaultValue
     */
    function getNearestAttributeValue(el, attributeName, defaultValue) {
        // walk up the dom tree looking for data-action and data-trigger
        while (el && el !== document.documentElement) {
            const value = el.getAttribute(attributeName);
            if (value)
                return value;

            el = el.parentNode;
        }
        return defaultValue;
    }

    // console.log('long-press-event: attaching event handlers')
    document.addEventListener('click', clickHandler, captureEventsOption);
    document.addEventListener('contextmenu', clickHandler, captureEventsOption);

    let hasPointerEvents = (('PointerEvent' in window) || (window.navigator && 'msPointerEnabled' in window.navigator));
    if (hasPointerEvents) {
        document.addEventListener('pointerdown', mouseDownHandler, captureEventsOption);
        document.addEventListener('pointerup', mouseUpHandler, captureEventsOption);
        document.addEventListener('pointermove', mouseMoveHandler, captureEventsOption);
    }
    else {
        document.addEventListener('touchstart', mouseDownHandler, captureEventsOption);
        document.addEventListener('mousedown', mouseDownHandler, captureEventsOption);
        document.addEventListener('touchend', mouseUpHandler, captureEventsOption);
        document.addEventListener('mouseup', mouseUpHandler, captureEventsOption);
        document.addEventListener('touchmove', mouseMoveHandler, captureEventsOption);
        document.addEventListener('mousemove', mouseMoveHandler, captureEventsOption);

        /* Old code:
        var isTouch = (('ontouchstart' in window) || (navigator.MaxTouchPoints > 0) || (navigator.msMaxTouchPoints > 0));

        // switch to pointer events or touch events if using a touch screen
        var mouseDown = hasPointerEvents ? 'pointerdown' : isTouch ? 'touchstart' : 'mousedown';
        var mouseUp = hasPointerEvents ? 'pointerup' : isTouch ? 'touchend' : 'mouseup';
        var mouseMove = hasPointerEvents ? 'pointermove' : isTouch ? 'touchmove' : 'mousemove';
        */
    }

    // hook events that clear a pending long press event
    document.addEventListener('wheel', clearLongPressTimer, captureEventsOption);
    document.addEventListener('scroll', clearLongPressTimer, captureEventsOption);

}(window, document));
