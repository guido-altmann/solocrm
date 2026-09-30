// Global keyboard shortcuts (SPEC 3.2). Reports shortcuts to a .NET handler, which maps them to actions
// (KeyboardShortcuts.cs). Only Ctrl/Cmd + K also works in inputs and dialogs.
const singleKeys = new Set(["n", "?"]);
const sequenceKeys = new Set(["h", "p", "k", "o"]);
const sequenceTimeoutMs = 1000;
const isMac = /Mac|iPhone|iPad/.test(navigator.platform);

let handler = null;
let sequenceStartedAt = 0;

function isEditable(target) {
    if (!(target instanceof HTMLElement)) {
        return false;
    }
    return target.isContentEditable || ["INPUT", "TEXTAREA", "SELECT"].includes(target.tagName);
}

function report(event, shortcut) {
    event.preventDefault();
    handler.invokeMethodAsync("OnShortcut", shortcut);
}

function onKeyDown(event) {
    if (handler === null || event.repeat) {
        return;
    }

    // Ctrl/Cmd + K opens the command palette everywhere, also in inputs and dialogs.
    if ((isMac ? event.metaKey : event.ctrlKey) && !event.altKey && !event.shiftKey && event.key.toLowerCase() === "k") {
        report(event, "palette");
        return;
    }

    if (event.ctrlKey || event.metaKey || event.altKey
        || isEditable(event.target) || document.querySelector(".mud-dialog-container")) {
        sequenceStartedAt = 0;
        return;
    }

    // "?" is a shifted key on most layouts; letters are compared case-insensitively.
    const key = event.key.length === 1 ? event.key.toLowerCase() : event.key;

    // "G" then H/P/K/O within one second navigates (G H = Heute, G P = Pipeline, …).
    const inSequence = sequenceStartedAt > 0 && Date.now() - sequenceStartedAt <= sequenceTimeoutMs;
    sequenceStartedAt = 0;
    if (inSequence && sequenceKeys.has(key)) {
        report(event, `g ${key}`);
        return;
    }
    if (key === "g") {
        sequenceStartedAt = Date.now();
        event.preventDefault();
        return;
    }

    if (singleKeys.has(key)) {
        report(event, key);
    }
}

/** @returns whether the platform uses Cmd instead of Ctrl, for the shortcut hints. */
export function register(dotNetHandler) {
    if (handler === null) {
        document.addEventListener("keydown", onKeyDown);
    }
    handler = dotNetHandler;
    return isMac;
}

export function unregister() {
    document.removeEventListener("keydown", onKeyDown);
    handler = null;
}
