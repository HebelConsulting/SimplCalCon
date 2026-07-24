// Interactive avatar cropper (ADR 0035): a fixed square frame (the canvas) with the image
// pan + zoomable behind it. The visible frame IS the crop, so `toPng` just re-draws the same
// framed region at the target size. The server still only ever receives a clean square PNG.
export async function create(canvas, dataUrl) {
    const img = await loadImage(dataUrl);
    const frame = canvas.width; // square canvas, CSS-sized to match its pixel size
    const ctx = canvas.getContext('2d');

    // At zoom 1 the image's shorter side exactly fills the frame ("cover").
    const fit = frame / Math.min(img.width, img.height);
    const state = { zoom: 1, cx: img.width / 2, cy: img.height / 2 };

    const effectiveScale = () => fit * state.zoom;
    const sourceSide = () => frame / effectiveScale();

    function clampCenter() {
        const half = sourceSide() / 2;
        state.cx = half * 2 >= img.width ? img.width / 2 : Math.max(half, Math.min(img.width - half, state.cx));
        state.cy = half * 2 >= img.height ? img.height / 2 : Math.max(half, Math.min(img.height - half, state.cy));
    }

    function drawTo(target, size) {
        const s = sourceSide();
        target.getContext('2d').drawImage(img, state.cx - s / 2, state.cy - s / 2, s, s, 0, 0, size, size);
    }

    function render() {
        clampCenter();
        ctx.clearRect(0, 0, frame, frame);
        drawTo(canvas, frame);
    }

    let dragging = false;
    let lastX = 0;
    let lastY = 0;
    const onDown = (e) => { dragging = true; lastX = e.clientX; lastY = e.clientY; canvas.setPointerCapture(e.pointerId); };
    const onMove = (e) => {
        if (!dragging) return;
        state.cx -= (e.clientX - lastX) / effectiveScale();
        state.cy -= (e.clientY - lastY) / effectiveScale();
        lastX = e.clientX;
        lastY = e.clientY;
        render();
    };
    const onUp = () => { dragging = false; };
    const onWheel = (e) => { e.preventDefault(); setZoom(state.zoom * (e.deltaY < 0 ? 1.1 : 0.9)); };

    canvas.addEventListener('pointerdown', onDown);
    canvas.addEventListener('pointermove', onMove);
    canvas.addEventListener('pointerup', onUp);
    canvas.addEventListener('wheel', onWheel, { passive: false });

    function setZoom(z) {
        state.zoom = Math.max(1, Math.min(8, z));
        render();
    }

    render();

    return {
        setZoom,
        toPng(size) {
            const out = document.createElement('canvas');
            out.width = size;
            out.height = size;
            drawTo(out, size);
            return out.toDataURL('image/png');
        },
        dispose() {
            canvas.removeEventListener('pointerdown', onDown);
            canvas.removeEventListener('pointermove', onMove);
            canvas.removeEventListener('pointerup', onUp);
            canvas.removeEventListener('wheel', onWheel);
        },
    };
}

function loadImage(src) {
    return new Promise((resolve, reject) => {
        const img = new Image();
        img.onload = () => resolve(img);
        img.onerror = reject;
        img.src = src;
    });
}
