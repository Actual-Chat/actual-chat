export function setSettings() {
    const meta = document.createElement('meta');
    meta.name = "color-scheme";
    meta.content = "only light";
    document.getElementsByTagName('head')[0].appendChild(meta);

    let vh = window.innerHeight * 0.01;
    document.documentElement.style.setProperty('--vh', `${vh}px`);
}
