import { debounceTime, fromEvent, Subject, takeUntil } from 'rxjs';

import { Log } from 'logging';
import { setTimeout } from 'timerQueue';

const { debugLog } = Log.get('Landing');

export class LandingVideoModal {
    private readonly disposed$ = new Subject<void>();
    private readonly videoWrapper: HTMLElement;
    private readonly controlWrapper: HTMLElement;
    private readonly video: HTMLVideoElement;
    private readonly plug: HTMLImageElement;
    private readonly controlHeader: HTMLElement;
    private readonly controlFooter: HTMLElement;
    private readonly controlBody: HTMLElement;
    private readonly playBtn: HTMLElement;
    private readonly pauseBtn: HTMLElement;
    private readonly progressBar: HTMLProgressElement;
    private readonly timeline: HTMLElement;

    private isVideoPlayStarted = false;

    static create(landingVideoModal: HTMLElement): LandingVideoModal {
        return new LandingVideoModal(landingVideoModal);
    }

    constructor(
        private readonly landingVideoModal: HTMLElement,
    ) {
        this.videoWrapper = landingVideoModal.querySelector('.video-wrapper');
        this.video = this.videoWrapper.querySelector('.c-video');
        this.controlWrapper = landingVideoModal.querySelector('.control-wrapper');
        this.controlHeader = this.controlWrapper.querySelector('.c-header');
        this.controlFooter = this.controlWrapper.querySelector('.c-footer');
        this.controlBody = this.controlWrapper.querySelector('.c-body');
        this.playBtn = this.controlBody.querySelector('.play-btn');
        this.pauseBtn = this.controlBody.querySelector('.pause-btn');
        this.progressBar = this.controlFooter.querySelector('.c-progress-bar');
        this.timeline = this.controlFooter.querySelector('.c-timeline');

        this.showControl(true);
        const durationDiv = this.controlFooter.querySelector('.c-duration');
        durationDiv.innerHTML = this.formatTime(46);
        const currentTimeDiv = this.controlFooter.querySelector('.c-current');
        currentTimeDiv.innerHTML = this.formatTime(0);

        this.plug = this.landingVideoModal.querySelector('.c-video-plug') as HTMLImageElement;

        if (this.video != null) {
            fromEvent(document, 'touchend', { passive: false, once: true })
                .pipe(takeUntil(this.disposed$))
                .subscribe((event: TouchEvent) => this.onTouchEnd(event));

            fromEvent(this.video, 'playing')
                .pipe(takeUntil(this.disposed$))
                .subscribe(() => this.onVideoState());

            fromEvent(this.video, 'pause')
                .pipe(takeUntil(this.disposed$))
                .subscribe(() => this.onVideoState());

            fromEvent(this.video, 'timeupdate')
                .pipe(takeUntil(this.disposed$))
                .subscribe(() => this.updateTimeline());

            fromEvent(this.progressBar, 'click')
                .pipe(takeUntil(this.disposed$))
                .subscribe((event: MouseEvent) => this.seekVideoPoint(event));

            let clickElements = [this.pauseBtn, this.playBtn];
            fromEvent(clickElements, 'click')
                .pipe(takeUntil(this.disposed$))
                .subscribe((event: Event) => this.playOrPause(event));

            fromEvent(this.videoWrapper, 'click')
                .pipe(takeUntil(this.disposed$))
                .subscribe(() => this.showControl(true));

            fromEvent(this.controlWrapper, 'click')
                .pipe(takeUntil(this.disposed$))
                .subscribe(() => this.showControl(false));

            this.video.oncanplay = _ => {
                this.video.muted = true;
                this.video.play().then(() => {
                    let durationDiv = this.controlFooter.querySelector('.c-duration');
                    durationDiv.innerHTML = this.formatTime(this.video.duration);
                    this.plug.classList.remove('flex');
                    this.plug.hidden = true;
                    this.video.hidden = false;
                });
            };
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

    private showControl(show: boolean) {
        if (show)
            this.controlWrapper.style.display = 'flex';
        else
            this.controlWrapper.style.display = 'none';
    }

    private onVideoState() {
        if (this.video.paused) {
            this.controlBody.classList.remove('playing');
        } else {
            if (!this.controlBody.classList.contains('playing'))
                this.controlBody.classList.add('playing');
        }
    }

    private playOrPause(e: Event) {
        e.stopPropagation();
        if (this.video.paused)
            this.video.play().then(() => {
                setTimeout(() => {
                    this.showControl(false);
                }, 200);
            });
        else {
            this.video.pause();
            this.showControl(false);
        }
    }

    private updateTimeline() {
        let current = this.video.currentTime;
        let percentage = Math.floor((100 / this.video.duration) * current);
        this.progressBar.value = percentage;
        this.progressBar.innerHTML = percentage + '% played';
        let currentTimeDiv = this.controlFooter.querySelector('.c-current');
        currentTimeDiv.innerHTML = this.formatTime(current);
    }

    private seekVideoPoint(e: MouseEvent) {
        e.stopPropagation();
        let progressBar = this.progressBar;
        let percent = e.offsetX / progressBar.offsetWidth;
        this.video.currentTime = percent * this.video.duration;
        let value = progressBar.value = Math.floor(percent / 100);
        progressBar.innerHTML = value + '% played';
    }

    private onTouchEnd(event: TouchEvent): void {
        if (this.isVideoPlayStarted)
            return;

        this.video.muted = true;
        void this.video.play().then(_ => {
            this.isVideoPlayStarted = true;
            const durationDiv = this.controlFooter.querySelector('.c-duration');
            durationDiv.innerHTML = this.formatTime(this.video.duration);
            this.plug.classList.remove('flex');
            this.plug.hidden = true;
            this.video.hidden = false;
            debugLog.log('onTouchEnd: tutorial video playback started.');
        });
        debugLog.log('onTouchEnd: tutorial video play...');
    }
}
