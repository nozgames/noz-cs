// NoZ Game Loop

let dotNetRef = null;
let animationFrameId = null;
let lastTime = 0;
let running = false;
export function start(dotNet) {
    dotNetRef = dotNet;
    running = true;
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

function tick(currentTime) {
    if (!running) return;

    // Schedule next frame first so an exception in GameTick can't break the loop
    animationFrameId = requestAnimationFrame(tick);

    const deltaTime = (currentTime - lastTime) / 1000.0; // Convert to seconds
    lastTime = currentTime;

    // Call C# game tick
    dotNetRef.invokeMethod('GameTick', deltaTime);
}
