// NoZ Game Loop

let dotNetRef = null;
let animationFrameId = null;
let lastTime = 0;
let running = false;
let paused = false;

export function start(dotNet) {
    dotNetRef = dotNet;
    running = true;
    paused = false;
    lastTime = performance.now();
    requestAnimationFrame(tick);
}

export function stop() {
    running = false;
    if (animationFrameId !== null) {
        cancelAnimationFrame(animationFrameId);
        animationFrameId = null;
    }
}

// Called by noz-platform.js when tab visibility changes
export function setPaused(isPaused) {
    paused = isPaused;
    if (!isPaused) {
        // Reset lastTime so the first frame back doesn't get a huge deltaTime
        lastTime = performance.now();
    }
}

function tick(currentTime) {
    if (!running) return;

    // Schedule next frame first so an exception in GameTick can't break the loop
    animationFrameId = requestAnimationFrame(tick);

    // Skip calling into C# while paused (tab hidden) — RAF is throttled but can
    // still fire at ~1fps in some browsers, and WebGPU can't render when hidden
    if (paused) {
        lastTime = currentTime;
        return;
    }

    const deltaTime = (currentTime - lastTime) / 1000.0; // Convert to seconds
    lastTime = currentTime;

    // Call C# game tick
    try {
        dotNetRef.invokeMethod('GameTick', deltaTime);
    } catch (e) {
        console.error('[GameLoop] GameTick threw:', e);
    }
}
