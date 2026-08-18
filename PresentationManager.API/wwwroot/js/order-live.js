// Connects to PresentationOrderHub using the browser's own auth cookie (same-origin, sent automatically -
// no token wiring needed here, unlike the WinForms client). Only the Judge "Project" view sets
// data-project-id on <body>; every other page just leaves the connection idle.
(function () {
    if (typeof signalR === "undefined") {
        return;
    }

    var connection = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/presentation-order")
        .withAutomaticReconnect()
        .build();

    connection.on("OrderRandomized", function (projectId) {
        var current = document.body.dataset.projectId;
        if (!current || Number(current) !== Number(projectId)) {
            return;
        }

        // A page can opt into an in-place update (no navigation) by defining this hook before this script's
        // connection.start() actually resolves - see order-shuffle.js on the OrderOperator stage, which
        // updates the queue via fetch so a fullscreen presentation never gets torn down by a reload. Anything
        // that doesn't define it (Judge's own project view) keeps the original reload-on-change behavior.
        if (typeof window.onPresentationOrderChanged === "function") {
            window.onPresentationOrderChanged(projectId);
        } else {
            window.location.reload();
        }
    });

    connection.start().catch(function () {
        // Silently degrades to a manual page refresh - same trade-off as AdminForm's hub client swallowing
        // a connect failure and relying on its background poll instead.
    });
})();
