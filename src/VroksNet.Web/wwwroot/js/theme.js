// Theme persistence for the "Ocean & Lime" light/dark toggle (NavMenu.razor). Kept as a plain
// global rather than a JS module — matches how the rest of this standalone Blazor WASM app calls
// into JS (IJSRuntime.InvokeAsync against a named function), no bundler here to import a module.
window.vroksnetTheme = {
    storageKey: "vroksnet-theme",

    /// Returns the active theme ("light"/"dark") — whatever the inline script in index.html
    /// already applied to <html data-bs-theme="...">, so this never disagrees with what's on screen.
    get: function () {
        return document.documentElement.getAttribute("data-bs-theme") || "light";
    },

    set: function (theme) {
        document.documentElement.setAttribute("data-bs-theme", theme);
        try {
            localStorage.setItem(this.storageKey, theme);
        } catch {
            // Private browsing / storage disabled — the toggle still works for this page load,
            // it just won't be remembered next visit.
        }
    }
};
