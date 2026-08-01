import { getLogs } from 'logging';

const { warnLog } = getLogs('FileDownload');

export const downloadFile = async (url: string, fileName: string): Promise<void> => {
    let blob: Blob | null = null;
    try {
        const response = await fetch(url);
        if (response.ok)
            blob = await response.blob();
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
        a.download = fileName || '';
        a.style.display = 'none';
        document.body.appendChild(a);
        a.click();
        a.remove();
    }
    finally {
        setTimeout(() => URL.revokeObjectURL(objectUrl), 0);
    }
};
