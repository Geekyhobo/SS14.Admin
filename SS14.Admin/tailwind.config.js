/** @type {import('tailwindcss').Config} */
module.exports = {
    content: [
        './Components/**/*.{razor,cs,css}',
        './wwwroot/**/*.{html,css}',
    ],
    safelist: [
        'active-nav-link',
        'cursor-pointer',
        'components-reconnect-show',
        'components-reconnect-hide',
        'components-reconnect-failed',
    ],
    darkMode: 'class',
    theme: {
        extend: {
            screens: {
                'nav': '900px',
            },
            textColor: {
                'severity-medium': 'var(--color-severity-medium)',
                'severity-high': 'var(--color-severity-high)',
                'severity-extreme': 'var(--color-severity-extreme)',
            },
            minHeight: {
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
