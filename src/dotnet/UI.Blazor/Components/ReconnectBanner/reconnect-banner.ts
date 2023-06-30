import { Subject, } from 'rxjs';

import { Log } from 'logging';
const { debugLog } = Log.get('ReconnectBanner');

export class ReconnectBanner {
    private readonly disposed$ = new Subject<void>();
    private readonly banner: HTMLElement;
    private readonly blazorRef: DotNet.DotNetObject;
    private readonly timerDiv: HTMLElement;
    private timeStart: number = 10;

    static create(banner: HTMLElement, blazorRef: DotNet.DotNetObject): ReconnectBanner {
        return new ReconnectBanner(banner, blazorRef);
    }

    constructor(banner: HTMLElement, blazorRef: DotNet.DotNetObject) {
        this.banner = banner;
        this.blazorRef = blazorRef;
        this.timerDiv = this.banner.querySelector('.reconnect-timer');
        if (this.timerDiv == null) {
            throw new DOMException("Timer div not found.");
        }
        this.timerDiv.innerHTML = this.timeStart.toString();
        this.countdown();
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;
        this.disposed$.next();
        this.disposed$.complete();
    }

    private countdown() {
        let timeLeft = this.timeStart;
        let timer = setInterval(function(){
            if (timeLeft < 1) {
                clearInterval(timer);
            }
            timeLeft -= 1;
            let timerDiv = document.querySelectorAll('.reconnect-timer');
            timerDiv.forEach(div => {
                div.innerHTML = timeLeft.toString();
            })
        }, 1000);
    }
}
