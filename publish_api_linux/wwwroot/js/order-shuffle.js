// OrderOperator's randomize screen (Order/Project.cshtml):
// 1. Fullscreen toggle on the whole "stage" (title + queue + button), for projecting the live draw.
// 2. A short client-side shuffle animation on the visible names before the real submit - the actual order is
//    still decided server-side by PresentationQueueService.RandomizeOrderAsync (this never fabricates the
//    real result), the animation just makes the "random" part of a random draw visible instead of an instant
//    swap, then hands off to a normal form submit/page reload for the real, persisted order.
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

            var updateFsBtn = function () {
                var active = (document.fullscreenElement || document.webkitFullscreenElement) === stage;
                fsBtn.classList.toggle("is-active", active);
                var label = fsBtn.querySelector(".fullscreen-label");
                if (label) {
                    label.textContent = active ? "Ekrandan chiqish" : "To'liq ekran";
                }
            };
            document.addEventListener("fullscreenchange", updateFsBtn);
            document.addEventListener("webkitfullscreenchange", updateFsBtn);
        }
    }

    var form = document.getElementById("randomizeForm");
    var queueList = document.getElementById("queueList");
    if (!form || !queueList) {
        return;
    }

    form.addEventListener("submit", function (e) {
        if (form.dataset.confirmed === "true") {
            return true;
        }
        e.preventDefault();

        if (!window.confirm("Joriy tartib almashtiriladi. Tasdiqlaysizmi?")) {
            return false;
        }

        runShuffleReveal(function () {
            form.dataset.confirmed = "true";
            var button = form.querySelector("button[type=submit]");
            if (button) {
                button.disabled = true;
                button.classList.remove("is-shuffling");
                button.textContent = "Shakllantirilmoqda...";
            }
            form.submit();
        });
    });

    function runShuffleReveal(done) {
        var rows = Array.prototype.slice.call(queueList.querySelectorAll(".queue-name"));
        if (rows.length < 2) {
            done();
            return;
        }

        var button = form.querySelector("button[type=submit]");
        if (button) {
            button.classList.add("is-shuffling");
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
            rows.forEach(function (el, idx) { el.textContent = names[idx]; });

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
})();
