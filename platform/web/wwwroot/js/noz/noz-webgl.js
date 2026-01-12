// NoZ WebGL Render Backend

let gl = null;
let canvas = null;

export function init() {
    canvas = document.getElementById('noz-canvas');
    if (!canvas) {
        throw new Error('Canvas not found. Make sure platform is initialized first.');
    }

    // Try WebGL2 first, fall back to WebGL1
    gl = canvas.getContext('webgl2');
    if (!gl) {
        gl = canvas.getContext('webgl');
        if (!gl) {
            throw new Error('WebGL not supported');
        }
        console.log('NoZ: Using WebGL 1.0');
    } else {
        console.log('NoZ: Using WebGL 2.0');
    }

    // Set default state
    gl.enable(gl.BLEND);
    gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);

    return true;
}

export function shutdown() {
    gl = null;
}

export function beginFrame() {
    // Reset state at the start of each frame if needed
}

export function endFrame() {
    // WebGL automatically presents, but flush ensures commands are submitted
    gl.flush();
}

export function clear(r, g, b, a) {
    gl.clearColor(r, g, b, a);
    gl.clear(gl.COLOR_BUFFER_BIT);
}

export function setViewport(x, y, width, height) {
    gl.viewport(x, y, width, height);
}

// Future: texture, shader, draw operations
export function createTexture(width, height, data) {
    const texture = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, width, height, 0, gl.RGBA, gl.UNSIGNED_BYTE, data);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    return texture;
}

export function deleteTexture(texture) {
    gl.deleteTexture(texture);
}

// Get the WebGL context for advanced use
export function getContext() {
    return gl;
}
