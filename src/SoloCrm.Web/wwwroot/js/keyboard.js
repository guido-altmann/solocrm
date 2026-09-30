// Global keyboard shortcuts (SPEC 3.2). Reports shortcuts to a .NET handler.
const shortcutKeys = new Set(["n"]);

let handler = null;

function isEditable(target) {
    if (!(target instanceof HTMLElement)) {
        return false;
    }
    return target.isContentEditable || ["INPUT", "TEXTAREA", "SELECT"].includes(target.tagName);
}

const isMac = /Mac|iPhone|iPad/.test(navigator.platform);

function onKeyDown(event) {
    if (handler === null || event.repeat) {
        return;
    }

    // Ctrl/Cmd + K opens the command palette everywhere, also in inputs and dialogs.
    if ((isMac ? event.metaKey : event.ctrlKey) && !event.altKey && !event.shiftKey && event.key.toLowerCase() === "k") {
        event.preventDefault();
        handler.invokeMethodAsync("OnShortcut", "palette");
        return;
    }

    if (event.ctrlKey || event.metaKey || event.altKey) {
        return;
    }
    if (isEditable(event.target) || document.querySelector(".mud-dialog-container")) {
        return;
    }

    const key = event.key.toLowerCase();
    if (!shortcutKeys.has(key)) {
        return;
    }

    event.preventDefault();
    handler.invokeMethodAsync("OnShortcut", key);
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
