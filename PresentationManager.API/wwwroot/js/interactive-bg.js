// Site-wide background, layered under .bg-orbs (see site.css):
// 1. Populates .bg-stars once, up front, with a sparse twinkling starfield - the same idea as the
//    OrderOperator fullscreen stage's own denser .stage-stars (order-shuffle.js), just fewer/smaller/dimmer
//    so it reads as ambient texture behind admin tables/cards rather than competing with them.
// 2. Drives the background's cursor-tracking spotlight (the first radial-gradient layer on body) AND a small
//    parallax shift on .bg-stars, both from the same pointer position - the one genuinely *interactive* part
//    of the background. --spot-x/--spot-y (percentages) feed the gradient's own "at X Y" position;
//    --parallax-x/--parallax-y (plain px) feed .bg-stars' translate, kept as separate custom properties so
//    the two consumers don't have to share a unit system. rAF-throttled so this never fires more than once
//    per frame regardless of how many pointermove events the browser delivers. Skipped entirely under
//    prefers-reduced-motion, same as the rest of this app's motion (aurora-drift, the order-shuffle reveal) -
//    the gradient and star layer just sit at their CSS default/center position instead.
(function () {
    var starsContainer = document.getElementById("bgStars");
    if (starsContainer) {
        // Same count/size/timing ranges as the OrderOperator fullscreen stage's own starfield
        // (order-shuffle.js's #stageStars) - the whole site now shares one background treatment instead of
        // fullscreen looking like a different product from everywhere else.
        var starsHtml = "";
        var starCount = 70;
        for (var s = 0; s < starCount; s++) {
            var top = (Math.random() * 100).toFixed(2);
            var left = (Math.random() * 100).toFixed(2);
            var size = (Math.random() * 2 + 1.5).toFixed(1);
            var duration = (Math.random() * 3 + 2.2).toFixed(2);
            var delay = (Math.random() * 4).toFixed(2);
            starsHtml += '<span class="bg-star" style="top:' + top + '%;left:' + left + '%;'
                + 'width:' + size + 'px;height:' + size + 'px;'
                + 'animation-duration:' + duration + 's;animation-delay:' + delay + 's;"></span>';
        }
        starsContainer.innerHTML = starsHtml;
    }

    if (window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
        return;
    }

    var root = document.documentElement;
    var pending = false;
    var lastX = 50;
    var lastY = 50;

    function apply() {
        pending = false;
        root.style.setProperty("--spot-x", lastX.toFixed(1) + "%");
        root.style.setProperty("--spot-y", lastY.toFixed(1) + "%");

        // Small (a handful of px), inverted offset - the star layer drifts opposite the cursor, the usual
        // "background sits further away" parallax cue - capped low enough to read as a subtle depth effect,
        // never as the page itself shifting around.
        var parallaxX = ((lastX - 50) / 50) * -6;
        var parallaxY = ((lastY - 50) / 50) * -6;
        root.style.setProperty("--parallax-x", parallaxX.toFixed(2) + "px");
        root.style.setProperty("--parallax-y", parallaxY.toFixed(2) + "px");
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
