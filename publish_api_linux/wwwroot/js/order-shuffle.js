// OrderOperator's randomize screen (Order/Project.cshtml):
// 1. "To'liq ekran" opens a second, separate browser window at the same URL plus ?present=1 - not this same
//    tab's native Fullscreen API anymore - so the operator can keep working this page (queue already looks
//    the same either way, see the list/grid rules in site.css) while a projector shows that other window.
//    The query flag is what puts #orderStage into "presenting" mode (.is-presenting, added below on load) -
//    the stage chrome (starfield, big centered heading, hidden page title/buttons) only ever applies there.
// 2. "Ro'yxatni shakllantirish" runs a short client-side shuffle animation on the visible names while the
//    real POST is in flight, then redraws the queue from that response - all via fetch, never a real form
//    submit/page navigation, so a presenting window is never torn down by a reload. The actual order is
//    still decided server-side by PresentationQueueService.RandomizeOrderAsync; the animation only makes the
//    draw itself visible instead of an instant swap.
// 3. "Ro'yxatni tozalash" undoes any number of rehearsal draws the same way (fetch, no navigation), restoring
//    presenters to their original registration order via PresentationQueueService.ResetOrderAsync - no
//    shuffle animation for this one, it's a plain redraw.
// 4. window.onPresentationOrderChanged (called by order-live.js on SignalR's OrderRandomized event) redraws
//    the same way when a *different* tab/operator - or the desktop AdminForm's own drag-and-drop reorder -
//    changes this project's order, so a presenting window left open stays live without ever navigating away.
(function () {
    var stage = document.getElementById("orderStage");

    // A separate popup window is what actually "presents" now (see the class comment above); this page's
    // own copy just needs to know it's the one, which a plain ?present=1 URL flag (set by the opener's own
    // fullscreenBtn click below) says as plainly as anything - checked and applied before anything else
    // runs so the very first paint already has the right layout, not a flash of the outer chrome first.
    var isPresentingWindow = new URLSearchParams(window.location.search).get("present") === "1";
    if (isPresentingWindow) {
        // On <body> too, not just #orderStage - the sidebar and the .container's own centered/padded width
        // live outside #orderStage, in _Layout.cshtml, so hiding/unsetting those (see body.is-presenting in
        // site.css) needs a hook reachable from there. This is what makes the popup show nothing but the
        // stage, edge to edge, same as the old native Fullscreen API used to - not this same page's ordinary
        // sidebar+padded layout with the stage merely sitting inside it.
        document.body.classList.add("is-presenting");
        if (stage) {
            stage.classList.add("is-presenting");

            // .is-presenting styling alone only makes this tab's own content look right - the browser's own
            // address bar/tabs are still there unless this tab is *actually* put into the native Fullscreen
            // API, same as the old in-place toggle used to do (just on this tab's own stage now). Opening
            // via a real clicked link (see fsBtn/Dashboard.cshtml) carries a "this came from a genuine user
            // gesture" status into the new tab far more reliably than window.open() ever did, so this often
            // succeeds immediately with no further interaction - but no browser guarantees that outright, so
            // #stageFullscreenBtn (shown only while not yet actually fullscreen) is a real, visible click
            // target to finish the job instead of leaving the operator to guess that some click will do it.
            var requestFullscreen = stage.requestFullscreen || stage.webkitRequestFullscreen;
            var isActuallyFullscreen = function () {
                return !!(document.fullscreenElement || document.webkitFullscreenElement);
            };
            var tryEnterFullscreen = function () {
                if (!requestFullscreen || isActuallyFullscreen()) {
                    return;
                }
                var result = requestFullscreen.call(stage);
                if (result && result.catch) {
                    result.catch(function () {});
                }
            };

            var stageFsBtn = document.getElementById("stageFullscreenBtn");
            var updateStageFsBtn = function () {
                if (stageFsBtn) {
                    stageFsBtn.style.display = isActuallyFullscreen() ? "none" : "";
                }
            };
            if (stageFsBtn) {
                stageFsBtn.addEventListener("click", tryEnterFullscreen);
            }
            document.addEventListener("fullscreenchange", updateStageFsBtn);
            document.addEventListener("webkitfullscreenchange", updateStageFsBtn);

            tryEnterFullscreen();
            updateStageFsBtn();
        }
    }

    var fsBtn = document.getElementById("fullscreenBtn");
    if (fsBtn) {
        fsBtn.addEventListener("click", function () {
            var url = new URL(window.location.href);
            url.searchParams.set("present", "1");

            // A synthetic click on a real <a target="_blank"> - not window.open() - since browsers carry a
            // clicked link's own "this came from a real user gesture" status into the tab it opens far more
            // reliably than a script-driven window.open() ever does, which is what the new tab's own
            // requestFullscreen() attempt on load (see isPresentingWindow below) actually depends on to
            // succeed immediately instead of needing a click inside that tab first. Same approach
            // Dashboard.cshtml's own project links use for exactly this reason.
            var link = document.createElement("a");
            link.href = url.toString();
            link.target = "_blank";
            link.rel = "noopener";
            document.body.appendChild(link);
            link.click();
            document.body.removeChild(link);
        });
    }

    // Populates the presenting theme's starfield backdrop once, up front - the container itself is
    // display:none outside .is-presenting (see .stage-stars in site.css), so there's no cost to doing this
    // eagerly even on a window that never ends up presenting.
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

    // A plain top-to-bottom list of names, split into columns of QUEUE_TARGET_ROWS_PER_COLUMN each (e.g. 40
    // -> 4 columns of 10, 27 -> 3 columns of 9, 36 -> 4 columns of 9 - always evenly split, never a
    // shorter/longer column left over) so everyone fits within one screen's height with no scrolling - the
    // same rule in both the presenting tab and the outer operator panel (only the surrounding chrome differs
    // between them), so both read as the same list and both stay on one page. Deliberately a fixed target,
    // not derived from the current window's actual height: a presenting tab's real viewport height changes
    // the moment it actually enters fullscreen (see tryEnterFullscreen above), and computing column count
    // from whatever height happened to be true at that exact instant used to make the layout visibly jump
    // between "tab freshly opened" and "now truly fullscreen" - a fixed target keeps both looking identical.
    // Available height still matters as a pure safety net: if it can't actually fit that many rows at the
    // base font size (a short window, or many more presenters than usual), text shrinks just enough that it
    // still does, and width still caps the column count on a too-narrow screen. reserveBelow is measured
    // directly off whatever actually follows the list (the action buttons, plus - outer panel only - the
    // hint text under them) rather than a guessed constant, since that guess is what used to clip the last
    // row whenever the real footer came out taller than expected. Re-run after every redraw and on resize,
    // since row count/available space can change independently of each other and this page never navigates
    // away to naturally recompute it.
    var QUEUE_BASE_NAME_FONT = 22; // px - matches the CSS fallback and .queue-row's own padding below
    var QUEUE_BASE_INDEX_FONT = 18; // px
    var QUEUE_ROW_PADDING_Y = 28; // px - .queue-row's top+bottom padding (14px each) in site.css
    var QUEUE_ROW_GAP = 12; // px - --space-3, the grid's row-gap
    var QUEUE_COLUMN_GAP = 32; // px - --space-6, the grid's column-gap
    var QUEUE_MIN_COLUMN_WIDTH = 220; // px - below this, a full-length name has no reasonable room even wrapped
    var QUEUE_IDEAL_COLUMN_WIDTH = 340; // px - how wide a column gets when it isn't forced narrower by width
    var QUEUE_TARGET_ROWS_PER_COLUMN = 10; // fewer than this many presenters -> a single column

    // Sums the rendered height of whatever comes after the list itself (the action buttons row, and - only
    // in the outer panel, where they're not display:none - the hint/meta line under it) plus a little
    // breathing room, instead of guessing a fixed pixel figure that has no way to know how tall that footer
    // actually turned out in either context.
    function measureReserveBelow() {
        var reserve = 24;
        var sibling = queueList.nextElementSibling;
        while (sibling) {
            reserve += sibling.offsetHeight;
            sibling = sibling.nextElementSibling;
        }
        return reserve;
    }

    function updateQueueFullscreenLayout() {
        if (!stage) {
            return;
        }
        requestAnimationFrame(function () {
            var count = queueList.querySelectorAll(".queue-row").length;
            if (count === 0) {
                stage.style.removeProperty("--queue-fs-height");
                queueList.style.removeProperty("width");
                queueList.style.removeProperty("--queue-cols");
                queueList.style.removeProperty("--queue-rows");
                return;
            }

            // queueList's own width is only ever explicitly set below (never wider than this) - safe to
            // measure its clientWidth straight off it with no chicken-and-egg risk, since a narrower value
            // set on a previous run only ever shrinks it, never invalidates this measurement of the space
            // actually available to grow back into.
            var availableWidth = Math.max(320, queueList.clientWidth || stage.clientWidth);
            var maxColumnsByWidth = Math.max(1, Math.floor((availableWidth + QUEUE_COLUMN_GAP) / (QUEUE_MIN_COLUMN_WIDTH + QUEUE_COLUMN_GAP)));

            var top = queueList.getBoundingClientRect().top;
            var availableHeight = Math.max(240, window.innerHeight - top - measureReserveBelow());

            var nameFont = QUEUE_BASE_NAME_FONT;
            var indexFont = QUEUE_BASE_INDEX_FONT;
            var rowHeight = QUEUE_ROW_PADDING_Y + nameFont * 1.25; // single reading line at the base size

            // Fixed target, not derived from availableHeight - see the comment above this function for why.
            var idealColumns = Math.max(1, Math.ceil(count / QUEUE_TARGET_ROWS_PER_COLUMN));
            var columns = Math.min(idealColumns, maxColumnsByWidth);
            var rowsPerColumn = Math.ceil(count / columns); // evenly split across whatever columns this leaves

            var neededHeight = rowsPerColumn * rowHeight + (rowsPerColumn - 1) * QUEUE_ROW_GAP;
            if (neededHeight > availableHeight) {
                // rowsPerColumn rows at the base font size doesn't actually fit this window's height (either
                // a too-narrow screen capped columns below the ideal target, or the window itself is just
                // short) - shrink text just enough that they still all fit.
                var shrink = availableHeight / neededHeight;
                nameFont = Math.max(11, nameFont * shrink);
                indexFont = Math.max(9, indexFont * shrink);
                rowHeight = QUEUE_ROW_PADDING_Y + nameFont * 1.25;
                neededHeight = rowsPerColumn * rowHeight + (rowsPerColumn - 1) * QUEUE_ROW_GAP;
            }

            var neededWidth = columns * QUEUE_IDEAL_COLUMN_WIDTH + (columns - 1) * QUEUE_COLUMN_GAP;

            stage.style.setProperty("--queue-fs-height", Math.min(neededHeight, availableHeight) + "px");
            queueList.style.width = Math.min(neededWidth, availableWidth) + "px";
            queueList.style.setProperty("--queue-cols", String(columns));
            queueList.style.setProperty("--queue-rows", String(rowsPerColumn));
            queueList.style.setProperty("--queue-name-font", nameFont.toFixed(1) + "px");
            queueList.style.setProperty("--queue-index-font", indexFont.toFixed(1) + "px");
        });
    }

    window.addEventListener("resize", updateQueueFullscreenLayout);
    // Entering/exiting real fullscreen (see tryEnterFullscreen above) usually fires its own resize too, but
    // not guaranteed on every browser - this covers it directly, since window.innerHeight/innerWidth (what
    // updateQueueFullscreenLayout actually measures) change substantially at that exact transition.
    document.addEventListener("fullscreenchange", updateQueueFullscreenLayout);
    document.addEventListener("webkitfullscreenchange", updateQueueFullscreenLayout);
    updateQueueFullscreenLayout();

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
        updateQueueFullscreenLayout();
    }

    // A-Z only (no digits/punctuation) - close enough to how every real name here is cased that the
    // flicker reads as "text", not noise, while still being unmistakably not a real name.
    var SCRAMBLE_CHARS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    function randomScramble(length) {
        var out = "";
        for (var i = 0; i < length; i++) {
            out += SCRAMBLE_CHARS.charAt(Math.floor(Math.random() * SCRAMBLE_CHARS.length));
        }
        return out;
    }

    // A short burst of small dots flying outward from a cell's center and fading - the "landed!" flourish
    // for the instant a position's real name locks in (see revealNext below). Absolutely positioned inside
    // the row itself (which needs position:relative and overflow:visible for this - see .queue-row in
    // site.css) and removed from the DOM once its animation finishes, rather than reused/hidden, since a
    // fresh burst per reveal is cheap and this never needs to run more than ~1 at a time per row anyway.
    function spawnParticleBurst(row) {
        var burst = document.createElement("div");
        burst.className = "queue-burst";
        var count = 10;
        for (var p = 0; p < count; p++) {
            var angle = (360 / count) * p + (Math.random() * 18 - 9);
            var distance = 30 + Math.random() * 24;
            var rad = (angle * Math.PI) / 180;
            var dot = document.createElement("span");
            dot.className = "queue-burst-dot";
            dot.style.setProperty("--dx", (Math.cos(rad) * distance).toFixed(1) + "px");
            dot.style.setProperty("--dy", (Math.sin(rad) * distance).toFixed(1) + "px");
            dot.style.setProperty("--burst-delay", Math.round(Math.random() * 50) + "ms");
            burst.appendChild(dot);
        }
        row.appendChild(burst);
        setTimeout(function () {
            if (burst.parentNode) {
                burst.parentNode.removeChild(burst);
            }
        }, 800);
    }

    // Who's actually landed in which spot is the whole suspense of a random draw, so this runs in two
    // overlapping phases instead of one flat delay-then-swap-everything-at-once:
    // 1. Every cell flickers through random letters at once (never a real name - see randomScramble/
    //    is-scrambling in site.css, which also drives the blur+shimmer look), for one collective beat so
    //    the whole grid visibly "starts spinning" together.
    // 2. Positions then lock in one at a time, in order (1st, 2nd, 3rd, ...) rather than all together -
    //    each stops scrambling, shows its real name, and gets its own little particle burst - so the draw
    //    reads as a sequence of moments instead of an instant reveal. Cells not yet locked in keep
    //    scrambling the entire time this runs.
    function runShuffleReveal(names, done) {
        var rowEls = Array.prototype.slice.call(queueList.querySelectorAll(".queue-row"));
        var nameEls = rowEls.map(function (row) { return row.querySelector(".queue-name"); });

        if (!names || names.length !== rowEls.length || rowEls.length < 2) {
            if (names) {
                renderQueue(names);
            }
            done();
            return;
        }

        var lengths = nameEls.map(function (el) { return Math.max(6, el.textContent.length); });
        var revealed = rowEls.map(function () { return false; });

        for (var r = 0; r < rowEls.length; r++) {
            rowEls[r].classList.add("is-scrambling");
            rowEls[r].style.setProperty("--shuffle-delay", (r % 10) * 90 + "ms");
        }

        if (stage) {
            stage.classList.add("is-drawing");
        }
        queueList.classList.add("shuffling");

        var scrambleTicker = setInterval(function () {
            for (var i = 0; i < nameEls.length; i++) {
                if (!revealed[i]) {
                    nameEls[i].textContent = randomScramble(lengths[i]);
                }
            }
        }, 55);

        var revealStagger = 90; // ms between each position locking in

        function revealNext(i) {
            if (i >= rowEls.length) {
                clearInterval(scrambleTicker);
                queueList.classList.remove("shuffling");
                if (stage) {
                    stage.classList.remove("is-drawing");
                }
                done();
                return;
            }

            revealed[i] = true;
            var row = rowEls[i];
            row.classList.remove("is-scrambling");
            nameEls[i].textContent = names[i];
            row.classList.add("just-revealed");
            spawnParticleBurst(row);
            setTimeout(function () {
                row.classList.remove("just-revealed");
            }, 700);

            setTimeout(function () {
                revealNext(i + 1);
            }, revealStagger);
        }

        // One collective beat of every cell scrambling together before positions start locking in.
        setTimeout(function () {
            revealNext(0);
        }, 1200);
    }

    // Guards against: (a) either action firing while the other is already in flight, (b) a double-fire from
    // binding both "click" and "submit" as a fallback on each button, and (c) the SignalR broadcast either
    // action's own POST triggers (sent to Clients.All, including the sender) landing on
    // window.onPresentationOrderChanged while that same call is already updating the DOM itself.
    var busy = false;

    // Once a draw actually completes, Randomize locks itself (disabled + dimmed, see .is-locked in
    // site.css) so it can't be re-run by accident on top of an order that's already set - "Ro'yxatni
    // tozalash" (Reset) is the only way to unlock it again, via the onFinishSuccess callback each
    // wireAction call below is given.
    var randomizeButton = document.getElementById("randomizeBtn");

    function setRandomizeLocked(locked) {
        if (!randomizeButton) {
            return;
        }
        randomizeButton.classList.toggle("is-locked", locked);
        // Not left to setBusy's own disabled handling below - this runs from onFinishSuccess, straight
        // after that same request's setBusy(false) already ran, so without this the button would be
        // visually dimmed (is-locked's own CSS) yet still actually clickable for a moment.
        randomizeButton.disabled = locked || busy;
    }

    // Wires one <form>/<button> pair (Randomize or Reset) up to a fetch-based, non-navigating submit.
    // withReveal=true runs the shuffle animation first (Randomize only); Reset just redraws directly.
    // onFinishSuccess (optional) fires once the whole action - including the reveal animation, for
    // Randomize - has actually finished, not just once the POST itself resolves.
    function wireAction(formId, buttonId, withReveal, onFinishSuccess) {
        var form = document.getElementById(formId);
        var button = document.getElementById(buttonId);
        if (!form || !button) {
            return;
        }

        function setBusy(value) {
            busy = value;
            // is-locked (Randomize only, see setRandomizeLocked) must survive setBusy(false) at the end of
            // a request - otherwise the button would flash back to enabled for a moment before the lock
            // logic below re-disables it.
            button.disabled = value || button.classList.contains("is-locked");
            button.classList.toggle("is-shuffling", value);
        }

        function submitAction() {
            if (busy || button.classList.contains("is-locked")) {
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
                        setBusy(false);
                        if (withReveal) {
                            announceOrderReady();
                        }
                        if (onFinishSuccess) {
                            onFinishSuccess();
                        }
                    }

                    if (withReveal) {
                        // Reveals straight from data.names itself (see runShuffleReveal) rather than a
                        // renderQueue call in finish() - the whole point is to lock names in one position at
                        // a time instead of swapping every cell to its final text in one go.
                        runShuffleReveal(data && data.names, finish);
                    } else {
                        if (data && data.names) {
                            renderQueue(data.names);
                        }
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

    wireAction("randomizeForm", "randomizeBtn", true, function () {
        setRandomizeLocked(true);
    });
    wireAction("resetForm", "resetBtn", false, function () {
        setRandomizeLocked(false);
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
