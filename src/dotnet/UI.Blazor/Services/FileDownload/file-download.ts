import { getLogs } from 'logging';

const { warnLog } = getLogs('FileDownload');

export const downloadFile = async (url: string, fileName: string): Promise<void> => {
    let blob: Blob | null = null;
    let serverFileName = '';
    try {
        const response = await fetch(url);
        if (response.ok) {
            blob = await response.blob();
            serverFileName = getContentDispositionFileName(response.headers.get('Content-Disposition'));
        }
        else
            warnLog?.log('downloadFile: HTTP', response.status);
    }
    catch (e) {
        warnLog?.log('downloadFile fetch failed:', e);
    }

    if (!blob) {
        window.open(url, '_blank', 'noopener');
        return;
    }

    const objectUrl = URL.createObjectURL(blob);
    try {
        const a = document.createElement('a');
        a.href = objectUrl;
        // The blob object-URL is same-origin, so `download` is honored; without a name the
        // browser falls back to the random object-URL id, so recover the server's name.
        a.download = fileName || serverFileName || '';
        a.style.display = 'none';
        document.body.appendChild(a);
        a.click();
        a.remove();
    }
    finally {
        setTimeout(() => URL.revokeObjectURL(objectUrl), 0);
    }
};

// Content-Disposition may carry the name twice: an ASCII `filename=` and an RFC 5987
// `filename*=UTF-8''...` holding the real, possibly non-ASCII name. Prefer the latter.
const getContentDispositionFileName = (header: string | null): string => {
    if (!header)
        return '';
    const extended = /filename\*=(?:UTF-8'')?([^;]+)/i.exec(header);
    if (extended)
        return decodeURIComponent(extended[1].trim().replace(/^"|"$/g, ''));
    const plain = /filename="?([^";]+)"?/i.exec(header);
    return plain ? plain[1].trim() : '';
};
