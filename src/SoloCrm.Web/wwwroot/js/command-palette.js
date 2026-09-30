// Command palette (US-13): keeps the caret in place when arrow keys move the selection,
// and scrolls the selected entry into view.
export function init(input) {
    input?.addEventListener("keydown", event => {
        if (event.key === "ArrowDown" || event.key === "ArrowUp") {
            event.preventDefault();
        }
    });
}

export function reveal(id) {
    document.getElementById(id)?.scrollIntoView({ block: "nearest" });
}
