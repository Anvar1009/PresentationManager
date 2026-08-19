// OrderOperator's randomize screen (Order/Project.cshtml):
// 1. Fullscreen toggle on the whole "stage" (title + queue + buttons) - CSS hides everything but the queue
//    and the action buttons while fullscreen, see .order-stage:fullscreen in site.css.
// 2. "Ro'yxatni shakllantirish" runs a short client-side shuffle animation on the visible names while the
//    real POST is in flight, then redraws the queue from that response - all via fetch, never a real form
//    submit/page navigation, so a fullscreen presentation is never torn down by a reload. The actual order is
//    still decided server-side by PresentationQueueService.RandomizeOrderAsync; the animation only makes the
//    draw itself visible instead of an instant swap.
// 3. "Jadvalni tozalash" undoes any number of rehearsal draws the same way (fetch, no navigation), restoring
//    presenters to their original registration order via PresentationQueueService.ResetOrderAsync - no
//    shuffle animation for this one, it's a plain redraw.
// 4. window.onPresentationOrderChanged (called by order-live.js on SignalR's OrderRandomized event) redraws
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

    // Populates the fullscreen theme's starfield backdrop once, up front - the container itself is
    // display:none outside :fullscreen (see .stage-stars in site.css), so there's no cost to doing this
    // eagerly rather than only the first time the operator actually goes fullscreen.
    var starsContainer = document.getElementById("stageStars");
    if (starsContainer) {
        var starsHtml = "";
        var starCount = 70;
        for (var s = 0; s < starCount; s++) {
            var top = (Math.random() * 100).toFixed(2);
            var left = (Math.random() * 100).toFixed(2);
            var size = (Math.random() * 2 + 1.5).toFixed(1);
            var duration = (Math.random() * 3 + 2.2).toFixed(2);
            var delay = (Math.random() * 4).toFixed(2);
            starsHtml += '<span class="stage-star" style="top:' + top + '%;left:' + left + '%;'
                + 'width:' + size + 'px;height:' + size + 'px;'
                + 'animation-duration:' + duration + 's;animation-delay:' + delay + 's;"></span>';
        }
        starsContainer.innerHTML = starsHtml;
    }

    var stageHeading = document.getElementById("stageHeading");
    var stageHeadingDefault = stageHeading ? stageHeading.textContent : "";
    var stageHeadingTimer = null;

    // Briefly swaps the fullscreen heading to announce a completed draw (mirrors how a real event's own
    // presentation screen would confirm "the order is set"), then reverts a few seconds later.
    function announceOrderReady() {
        if (!stageHeading) {
            return;
        }
        stageHeading.textContent = "Tartib shakllantirildi!";
        stageHeading.classList.add("is-announcing");
        clearTimeout(stageHeadingTimer);
        stageHeadingTimer = setTimeout(function () {
            stageHeading.textContent = stageHeadingDefault;
            stageHeading.classList.remove("is-announcing");
        }, 4000);
    }

    var queueList = document.getElementById("queueList");
    if (!queueList) {
        return;
    }

    // A handful of presenters just need a single centered column; 40+ need to be split across several so
    // everyone still fits on one screen with no scrolling. Rather than let CSS guess a column-width and
    // hope the browser's own balancing works out, this measures the actual rendered row height and the
    // actual available viewport space, then computes the exact column count needed to fit every row within
    // that height - column-fill:auto (see site.css) then just lays rows into that many columns in order.
    // Re-run after every redraw and on resize/fullscreen-change, since row count/available space can change
    // independently of each other and this page never navigates away to naturally recompute it.
    function updateQueueFullscreenHeight() {
        if (!stage) {
            return;
        }
        var isFullscreen = (document.fullscreenElement || document.webkitFullscreenElement) === stage;
        if (!isFullscreen) {
            stage.style.removeProperty("--queue-fs-height");
            queueList.style.removeProperty("column-count");
            return;
        }
        requestAnimationFrame(function () {
            var top = queueList.getBoundingClientRect().top;
            var reserveBelow = 160; // action button row + its own spacing below the queue
            var availableHeight = Math.max(240, window.innerHeight - top - reserveBelow);
            stage.style.setProperty("--queue-fs-height", availableHeight + "px");

            var rows = queueList.querySelectorAll(".queue-row");
            if (rows.length === 0) {
                queueList.style.removeProperty("column-count");
                return;
            }

            var rowBox = rows[0].getBoundingClientRect();
            var rowStyle = window.getComputedStyle(rows[0]);
            var rowOuterHeight = rowBox.height + parseFloat(rowStyle.marginBottom || "0");
            var rowsPerColumn = Math.max(1, Math.floor(availableHeight / Math.max(1, rowOuterHeight)));
            var neededColumns = Math.max(1, Math.ceil(rows.length / rowsPerColumn));
            queueList.style.setProperty("column-count", String(neededColumns));
        });
    }

    document.addEventListener("fullscreenchange", updateQueueFullscreenHeight);
    document.addEventListener("webkitfullscreenchange", updateQueueFullscreenHeight);
    window.addEventListener("resize", updateQueueFullscreenHeight);

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
        updateQueueFullscreenHeight();
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

    // Guards against: (a) either action firing while the other is already in flight, (b) a double-fire from
    // binding both "click" and "submit" as a fallback on each button, and (c) the SignalR broadcast either
    // action's own POST triggers (sent to Clients.All, including the sender) landing on
    // window.onPresentationOrderChanged while that same call is already updating the DOM itself.
    var busy = false;

    // Wires one <form>/<button> pair (Randomize or Reset) up to a fetch-based, non-navigating submit.
    // withReveal=true runs the shuffle animation first (Randomize only); Reset just redraws directly.
    function wireAction(formId, buttonId, withReveal) {
        var form = document.getElementById(formId);
        var button = document.getElementById(buttonId);
        if (!form || !button) {
            return;
        }

        function setBusy(value) {
            busy = value;
            button.disabled = value;
            button.classList.toggle("is-shuffling", value);
        }

        function submitAction() {
            if (busy) {
                return;
            }
            setBusy(true);

            // A hung request (flaky wifi on the venue's network, a proxy timeout, whatever) must not leave
            // the button permanently disabled until someone reloads the page - after 8s, give up waiting.
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
                    function finish() {
                        if (settled) {
                            return;
                        }
                        settled = true;
                        clearTimeout(safetyTimer);
                        if (data && data.names) {
                            renderQueue(data.names);
                        }
                        setBusy(false);
                        if (withReveal) {
                            announceOrderReady();
                        }
                    }

                    if (withReveal) {
                        runShuffleReveal(finish);
                    } else {
                        finish();
                    }
                })
                .catch(function () {
                    if (settled) {
                        return;
                    }
                    settled = true;
                    clearTimeout(safetyTimer);
                    setBusy(false);
                    // The AJAX path itself failed outright (proxy/network issue) - fall back to a plain form
                    // submit so the action still goes through this one time, even though a normal page
                    // navigation means it won't stay in fullscreen for it.
                    form.submit();
                });
        }

        // Bound on both the button's own click (primary path - prevents the native submit outright, so
        // nothing about the fullscreen document ever depends on a "submit" event actually reaching the form)
        // and the form's submit (fallback). submitAction() is idempotent via the busy guard, so both firing
        // is harmless.
        button.addEventListener("click", function (e) {
            e.preventDefault();
            submitAction();
        });
        form.addEventListener("submit", function (e) {
            e.preventDefault();
            submitAction();
        });
    }

    wireAction("randomizeForm", "randomizeBtn", true);
    wireAction("resetForm", "resetBtn", false);

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
