import { getLogs } from 'logging';

const { warnLog } = getLogs('LogView');

const NeighborCount = 5;

export class LogView {
    /** Returns the on-screen log entries plus `NeighborCount` items above and
     *  below them, formatted as plain text. The caller is expected to push the
     *  result to the clipboard via ClipboardUI — going through navigator.clipboard
     *  directly bypasses the MAUI/Android native bridge. */
    public static getVisibleEntriesText(): string {
        const logList = document.querySelector('.log-list');
        if (!logList) {
            warnLog?.log('getVisibleEntriesText: .log-list not found');
            return '';
        }

        const items = Array.from(logList.querySelectorAll<HTMLElement>('.item[data-key]'));
        if (items.length === 0)
            return '';

        const viewRect = logList.getBoundingClientRect();
        let firstVisible = -1;
        let lastVisible = -1;
        for (let i = 0; i < items.length; i++) {
            const r = items[i].getBoundingClientRect();
            if (r.bottom > viewRect.top && r.top < viewRect.bottom) {
                if (firstVisible === -1) firstVisible = i;
                lastVisible = i;
            }
        }
        if (firstVisible === -1)
            return '';

        const start = Math.max(0, firstVisible - NeighborCount);
        const end = Math.min(items.length - 1, lastVisible + NeighborCount);
        return items.slice(start, end + 1).map(formatLogItem).join('\n\n');
    }
}

function formatLogItem(item: HTMLElement): string {
    const timestamp = item.querySelector('.c-timestamp')?.textContent.trim() ?? '';
    const level = item.querySelector('.c-level')?.textContent.trim() ?? '';
    const category = item.querySelector('.c-category')?.textContent.trim() ?? '';
    const message = item.querySelector('.c-message')?.textContent.trim() ?? '';
    const exception = item.querySelector('.c-exception')?.textContent.trim() ?? '';

    const header = `${timestamp} ${level} [${category}]`;
    const body = exception ? `${message}\n${exception}` : message;
    return body ? `${header}\n${body}` : header;
}
