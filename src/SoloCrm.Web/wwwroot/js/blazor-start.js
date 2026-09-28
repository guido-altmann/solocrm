// Starts Blazor with a WebSocket-only circuit connection that skips SignalR negotiation.
// During a Coolify rolling update Traefik balances between the old and the new container;
// with negotiation, /_blazor/negotiate and the WebSocket could hit different containers and the
// circuit would never start (page stays prerendered and non-interactive). A single WebSocket
// request always lands on one container.
Blazor.start({
    circuit: {
        configureSignalR: builder => {
            builder.withUrl("_blazor", {
                skipNegotiation: true,
                transport: 1, // signalR.HttpTransportType.WebSockets
            });
        },
    },
});
