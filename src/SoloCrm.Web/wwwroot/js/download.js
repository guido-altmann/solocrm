// Offers text generated in the circuit as a file download (GDPR export, US-19). The content goes straight from
// the circuit to the browser; nothing is stored on the server.
export function downloadText(fileName, contentType, text) {
    const url = URL.createObjectURL(new Blob([text], { type: contentType }));
    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
}
