/**
 * Gets the client's preferences, i.e. dark mode and PII censoring.
 * @returns {ClientPreferences}
 */
window.getClientPreferences = () => {
    let darkModeOverride = localStorage.getItem("darkModeOverride");
    let darkMode;

    if (darkModeOverride === null) {
        // No override set, use system preference
        darkMode = window.matchMedia("(prefers-color-scheme: dark)").matches;
    } else {
        // User has explicitly set a preference
        darkMode = darkModeOverride === "true";
    }

    // Get PII censoring preference (defaults to false if not set)
    let censorPii = localStorage.getItem("censorPii") === "true";

    return {
        darkMode,
        censorPii
    };
}

/**
 * Applies or removes the 'dark' class on <html>.
 * Called from Blazor via JS interop when the user toggles dark mode.
 * @param {boolean} dark
 */
window.applyDarkMode = (dark) => {
    if (dark) {
        document.documentElement.classList.add("dark");
    } else {
        document.documentElement.classList.remove("dark");
    }
}
