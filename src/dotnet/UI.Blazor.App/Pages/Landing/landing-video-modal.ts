import { debounceTime, fromEvent, Subject, takeUntil } from 'rxjs';
import { ScreenSize } from '../../../UI.Blazor/Services/ScreenSize/screen-size';

import { Log } from 'logging';

const { debugLog } = Log.get('Landing');

export class LandingVideoModal {
    private readonly disposed$ = new Subject<void>();
    private readonly header: HTMLElement;
    private readonly footer: HTMLElement;
    private readonly body: HTMLElement;
    private readonly playBtn: HTMLElement;
    private readonly pauseBtn: HTMLElement;
    private readonly progressBar: HTMLProgressElement;
    private readonly timeline: HTMLElement;

    static create(landingVideoModal: HTMLElement): LandingVideoModal {
        return new LandingVideoModal(landingVideoModal);
    }

    constructor(
        private readonly landingVideoModal: HTMLElement,
    ) {
        this.header = landingVideoModal.querySelector('.c-header');
        this.footer = landingVideoModal.querySelector('.c-footer');
        this.body = landingVideoModal.querySelector('.c-body');
        this.playBtn = landingVideoModal.querySelector('.play-btn');
        this.pauseBtn = landingVideoModal.querySelector('.pause-btn');
        this.progressBar = landingVideoModal.querySelector('.c-progress-bar');
        this.timeline = landingVideoModal.querySelector('.c-timeline');

        // this.onScreenSizeChange();
        // ScreenSize.event$
        //     .pipe(takeUntil(this.disposed$))
        //     .subscribe(() => this.onScreenSizeChange());

        const plug = this.landingVideoModal.querySelector('.c-video-plug') as HTMLImageElement;
        const video = this.landingVideoModal.querySelector('.c-video') as HTMLVideoElement;
        if (video != null) {
            fromEvent(video, 'playing')
                .pipe(takeUntil(this.disposed$))
                .subscribe(() => this.onVideoState(video));

            fromEvent(video, 'pause')
                .pipe(takeUntil(this.disposed$))
                .subscribe(() => this.onVideoState(video));

            fromEvent(video, 'timeupdate')
                .pipe(takeUntil(this.disposed$))
                .subscribe(() => this.updateTimeline(video));

            fromEvent(this.progressBar, 'click')
                .pipe(takeUntil(this.disposed$))
                .subscribe((event: MouseEvent) => this.seekVideoPoint(event));

            let clickElements = [this.pauseBtn, this.playBtn];
            fromEvent(clickElements, 'click')
                .pipe(takeUntil(this.disposed$))
                .subscribe(() => this.playOrPause(video));

            video.play().then(() => {
                let durationDiv = this.footer.querySelector('.c-duration');
                durationDiv.innerHTML = this.formatTime(video.duration);
                plug.classList.remove('flex');
                plug.hidden = true;
                video.hidden = false;
                this.playBtn.classList.remove('invisible');
                this.pauseBtn.classList.remove('invisible');
                this.footer.classList.remove('invisible');
            });
        }
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    private formatTime(time: number) : string {
        let minutes = '';
        let seconds = '';
        let minNum = Math.floor((time / 60));
        let secNum = Math.round(time - (minNum * 60));
        if (minNum.toString().length < 2)
            minutes = `0${minNum}`;
        else
            minutes = minNum.toString();
        if (secNum.toString().length < 2)
            seconds = `0${secNum}`;
        else
            seconds = secNum.toString();
        return `${minutes}:${seconds}`;
    }

    private onVideoState = (video: HTMLVideoElement) => {
        if (video.paused) {
            this.body.classList.remove('playing');
        } else {
            if (!this.body.classList.contains('playing'))
                this.body.classList.add('playing');
        }
    }

    private playOrPause(video: HTMLVideoElement) {
        if (video.paused)
            video.play().then();
        else
            video.pause();
    }

    private updateTimeline(video: HTMLVideoElement) {
        let current = video.currentTime;
        console.log('current: ', current);
        let percentage = Math.floor((100 / video.duration) * current);
        this.progressBar.value = percentage;
        this.progressBar.innerHTML = percentage + '% played';
        let currentTimeDiv = this.footer.querySelector('.c-current');
        currentTimeDiv.innerHTML = this.formatTime(current);
    }

    private seekVideoPoint(e: MouseEvent) {
        let video = this.body.querySelector('video');
        let progressBar = this.progressBar;
        let percent = e.offsetX / progressBar.offsetWidth;
        video.currentTime = percent * video.duration;
        let value = progressBar.value = Math.floor(percent / 100);
        progressBar.innerHTML = value + '% played';
    }

    // Event handlers

    // private onScreenSizeChange() {
    //     const h = window.innerHeight;
    //     const w = window.innerWidth;
    //     const hwRatio = h / w;
    //     document.documentElement.style.setProperty('--wh', `${h}px`);
    //     let useFullScreenPages = ScreenSize.isNarrow() ? (hwRatio >= 1.8 && hwRatio <= 2.5) : (h >= 700);
    //     // if (useFullScreenPages)
    //     //     this.landing.classList.remove('no-full-screen-pages');
    //     // else
    //     //     this.landing.classList.add('no-full-screen-pages');
    // }
}
