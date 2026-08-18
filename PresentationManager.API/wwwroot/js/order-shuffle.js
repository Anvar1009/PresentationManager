// OrderOperator's randomize screen (Order/Project.cshtml):
// 1. Fullscreen toggle on the whole "stage" (title + queue + button) - CSS hides everything but the queue
//    and the randomize button while fullscreen, see .order-stage:fullscreen in site.css.
// 2. Clicking "Ro'yxatni shakllantirish" runs a short client-side shuffle animation on the visible names
//    while the real POST is in flight, then redraws the queue from that response - all via fetch, never a
//    real form submit/page navigation, so a fullscreen presentation is never torn down by a reload. The
//    actual order is still decided server-side by PresentationQueueService.RandomizeOrderAsync; the
//    animation only makes the draw itself visible instead of an instant swap.
// 3. window.onPresentationOrderChanged (called by order-live.js on SignalR's OrderRandomized event) redraws
//    the same way when a *different* tab/operator - or the desktop AdminForm's own drag-and-drop reorder -
//    changes this project's order, so a projector left open on this page in fullscreen stays live without
//    ever navigating away either.
(function () {
    var stage = document.getElementById("orderStage");
    var fsBtn = document.getElementById("fullscreenBtn");

    if (stage && fsBtn) {
        var supportsFullscreen = !!(stage.requestFullscreen || stage.webkitRequestFullscreen);
        if (!supportsFullscreen) {
            fsBtn.style.display = "none";
        } else {
            fsBtn.addEventListener("click", function () {
                var current = document.fullscreenElement || document.webkitFullscreenElement;
                if (!current) {
                    (stage.requestFullscreen || stage.webkitRequestFullscreen).call(stage);
                } else {
                    (document.exitFullscreen || document.webkitExitFullscreen).call(document);
                }
            });
        }
    }

    var form = document.getElementById("randomizeForm");
    var button = document.getElementById("randomizeBtn");
    var queueList = document.getElementById("queueList");
    if (!form || !button || !queueList) {
        return;
    }

    function escapeHtml(text) {
        var div = document.createElement("div");
        div.textContent = text;
        return div.innerHTML;
    }

    function renderQueue(names) {
        var html = "";
        for (var i = 0; i < names.length; i++) {
            var idx = String(i + 1);
            if (idx.length < 2) {
                idx = "0" + idx;
            }
            html += '<div class="queue-row"><span class="queue-index">' + idx + '</span>'
                + '<div class="queue-body"><div class="queue-name">' + escapeHtml(names[i]) + '</div></div></div>';
        }
        queueList.innerHTML = html;
    }

    function runShuffleReveal(done) {
        var rows = Array.prototype.slice.call(queueList.querySelectorAll(".queue-name"));
        if (rows.length < 2) {
            done();
            return;
        }

        var names = rows.map(function (el) { return el.textContent; });
        queueList.classList.add("shuffling");

        var ticks = 0;
        var maxTicks = 16;
        var delay = 70;

        function tick() {
            for (var i = names.length - 1; i > 0; i--) {
                var j = Math.floor(Math.random() * (i + 1));
                var tmp = names[i];
                names[i] = names[j];
                names[j] = tmp;
            }
            for (var k = 0; k < rows.length; k++) {
                rows[k].textContent = names[k];
            }

            ticks++;
            if (ticks < maxTicks) {
                delay += 14;
                setTimeout(tick, delay);
            } else {
                queueList.classList.remove("shuffling");
                done();
            }
        }

        tick();
    }

    // Guards against: (a) a double-fire from binding both "click" (below) and "submit" as a fallback, and
    // (b) the SignalR broadcast this tab's own Randomize call triggers (sent to Clients.All, including the
    // sender) landing on window.onPresentationOrderChanged while this same draw is already in flight.
    var busy = false;

    function setBusy(value) {
        busy = value;
        button.disabled = value;
        button.classList.toggle("is-shuffling", value);
    }

    function submitRandomize() {
        if (busy) {
            return;
        }
        setBusy(true);

        // A hung request (flaky wifi on the venue's network, a proxy timeout, whatever) must not leave the
        // button permanently disabled until someone reloads the page - after 8s, give up waiting and let the
        // operator try again.
        var settled = false;
        var safetyTimer = setTimeout(function () {
            if (!settled) {
                settled = true;
                setBusy(false);
            }
        }, 8000);

        fetch(form.action, {
            method: "POST",
            headers: { "X-Requested-With": "XMLHttpRequest" },
            body: new FormData(form)
        })
            .then(function (r) { return r.ok ? r.json() : Promise.reject(new Error("HTTP " + r.status)); })
            .then(function (data) {
                runShuffleReveal(function () {
                    if (settled) {
                        return;
                    }
                    settled = true;
                    clearTimeout(safetyTimer);
                    if (data && data.names) {
                        renderQueue(data.names);
                    }
                    setBusy(false);
                });
            })
            .catch(function () {
                if (settled) {
                    return;
                }
                settled = true;
                clearTimeout(safetyTimer);
                setBusy(false);
                // The AJAX path itself failed outright (proxy/network issue) - fall back to a plain form
                // submit so the randomize action still goes through this one time, even though a normal page
                // navigation means it won't stay in fullscreen for it.
                form.submit();
            });
    }

    // Bound on both the button's own click (primary path - prevents the native submit outright, so nothing
    // about the fullscreen document ever depends on a "submit" event actually reaching the form) and the
    // form's submit (fallback, in case something other than a direct click triggers it). submitRandomize()
    // is itself idempotent via the busy guard, so both firing is harmless.
    button.addEventListener("click", function (e) {
        e.preventDefault();
        submitRandomize();
    });
    form.addEventListener("submit", function (e) {
        e.preventDefault();
        submitRandomize();
    });

    window.onPresentationOrderChanged = function (projectId) {
        if (busy) {
            return;
        }
        fetch("/Order/Queue?projectId=" + encodeURIComponent(projectId), {
            headers: { "X-Requested-With": "XMLHttpRequest" }
        })
            .then(function (r) { return r.ok ? r.json() : null; })
            .then(function (data) {
                if (data && data.names) {
                    renderQueue(data.names);
                }
            })
            .catch(function () {});
    };
})();
