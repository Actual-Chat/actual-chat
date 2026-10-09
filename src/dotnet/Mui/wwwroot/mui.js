window.muiGetTimeZone = () => Intl.DateTimeFormat().resolvedOptions().timeZone;

// Inserts the text into the textarea inside the element with the given id, at the caret (replacing a selection)
window.muiInsertText = (containerId, text) => {
    const textarea = document.querySelector('#' + containerId + ' textarea');
    if (!textarea)
        return;

    const start = textarea.selectionStart ?? textarea.value.length;
    const end = textarea.selectionEnd ?? start;
    textarea.setRangeText(text, start, end, 'end');
    textarea.focus();
    textarea.dispatchEvent(new Event('input', { bubbles: true }));
    textarea.dispatchEvent(new Event('change', { bubbles: true }));
};
