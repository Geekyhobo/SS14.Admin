/** @type {import('tailwindcss').Config} */
/**
 * SS14.Admin Tailwind configuration.
 *
 * Breakpoint philosophy — desktop-first admin panel that stops blocking mobile.
 *
 *   sm  640   tablet portrait: forms start wrapping, 2-col grids allowed
 *   md  768   tablet landscape: most layouts can stop stacking
 *   nav 900   (custom) sidebar re-appears. 768 is too tight for sidebar + content,
 *             1024 too wide. 900 is where an admin sidebar + table actually fits.
 *   lg  1024  desktop: full density
 *   xl  1280  wide-desktop: form grids expand
 *
 * a `.dark` class on <html>. See Components/App.razor for the boot-time script.
 */
module.exports = {
    content: [
        // NOTE: tailwind.config.js lives at the repository root, but the actual
        // app source lives under ./SS14.Admin/. This should probably be changed
        './Components/**/*.{razor,css}',
        './Pages/**/*.{razor,html,cshtml}',
        './Shared/**/*.{razor,html}',
        './wwwroot/**/*.{razor,html,css}',
    ],
    safelist: [
        'active-nav-link',
        'cursor-pointer',
        // Keep Blazor reconnection overlay utilities reachable.
        'components-reconnect-show',
        'components-reconnect-hide',
        'components-reconnect-failed',
    ],
    darkMode: 'class',
    theme: {
        extend: {
            screens: {
                //breakpoint for the desktop sidebar.
                'nav': '900px',
            },
            textColor: {
                'severity-medium': 'var(--color-severity-medium)',
                'severity-high': 'var(--color-severity-high)',
                'severity-extreme': 'var(--color-severity-extreme)',
            },
            minHeight: {
                // Dynamic viewport height think ios
                'dvh': '100dvh',
            },
            height: {
                'dvh': '100dvh',
            },
            minWidth: {
                'touch': '2.5rem',
            },
            spacing: {
                'touch': '2.5rem',
            },
        },
    },
    plugins: [],
}
