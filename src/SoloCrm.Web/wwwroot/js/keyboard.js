// Global keyboard shortcuts (SPEC 3.2). Reports single-key shortcuts to a .NET handler.
const shortcutKeys = new Set(["n"]);

let handler = null;

function isEditable(target) {
    if (!(target instanceof HTMLElement)) {
        return false;
    }
    return target.isContentEditable || ["INPUT", "TEXTAREA", "SELECT"].includes(target.tagName);
}

function onKeyDown(event) {
    if (handler === null || event.repeat || event.ctrlKey || event.metaKey || event.altKey) {
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

export function register(dotNetHandler) {
    if (handler === null) {
        document.addEventListener("keydown", onKeyDown);
    }
    handler = dotNetHandler;
}

export function unregister() {
    document.removeEventListener("keydown", onKeyDown);
    handler = null;
}
