// Client-side normalization for profile photos (ADR 0035): take a (already downscaled)
// image data URL, crop the largest centered square, and draw it into a size×size PNG so
// the server only ever receives a clean 256×256 PNG and needs no image library.
export async function cropToSquarePng(dataUrl, size) {
    const img = await loadImage(dataUrl);
    const side = Math.min(img.width, img.height);
    const sx = (img.width - side) / 2;
    const sy = (img.height - side) / 2;

    const canvas = document.createElement('canvas');
    canvas.width = size;
    canvas.height = size;
    const ctx = canvas.getContext('2d');
    ctx.drawImage(img, sx, sy, side, side, 0, 0, size, size);
    return canvas.toDataURL('image/png');
}

function loadImage(src) {
    return new Promise((resolve, reject) => {
        const img = new Image();
        img.onload = () => resolve(img);
        img.onerror = reject;
        img.src = src;
    });
}
