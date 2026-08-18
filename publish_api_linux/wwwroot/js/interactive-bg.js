// Drives the background's cursor-tracking spotlight (the first radial-gradient layer on body, see site.css)
// by writing --spot-x/--spot-y onto the root element as the pointer moves. rAF-throttled so this never fires
// more than once per frame regardless of how many mousemove events the browser delivers. Skipped entirely
// under prefers-reduced-motion, same as the rest of this app's motion (aurora-drift, the order-shuffle
// reveal) - the gradient just sits at its CSS default center position instead.
(function () {
    if (window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
        return;
    }

    var root = document.documentElement;
    var pending = false;
    var lastX = 0;
    var lastY = 0;

    function apply() {
        pending = false;
        root.style.setProperty("--spot-x", lastX.toFixed(1) + "%");
        root.style.setProperty("--spot-y", lastY.toFixed(1) + "%");
    }

    window.addEventListener("pointermove", function (e) {
        lastX = (e.clientX / window.innerWidth) * 100;
        lastY = (e.clientY / window.innerHeight) * 100;
        if (!pending) {
            pending = true;
            requestAnimationFrame(apply);
        }
    }, { passive: true });
})();
