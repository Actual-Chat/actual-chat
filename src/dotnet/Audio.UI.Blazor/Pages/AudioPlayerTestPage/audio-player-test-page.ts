export class AudioPlayerTestPage {
    public static blockMainThread(milliseconds: number) {
        console.warn(`Block main thread for ${milliseconds}`);
        const start = new Date().getTime();
        while (true) {
            if (new Date().getTime() - start > milliseconds) {
                break;
            }
        }
        console.warn("Unblock main thread");
    }
}