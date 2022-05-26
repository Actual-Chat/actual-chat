export function setTheme(theme: string) {
    const body = document.body;
    if (theme === "Light")
        body.classList.remove('theme-dark')
    if (theme === "Dark")
        body.classList.add('theme-dark')
}
