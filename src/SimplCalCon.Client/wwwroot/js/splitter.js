// A draggable vertical divider between two flex panes (ADR 0036): dragging the handle sets
// the left pane's flex-basis (clamped), and the right pane takes the rest.
export function init(handle, left) {
    let dragging = false;
    const min = 220;

    const onDown = (e) => {
        dragging = true;
        handle.setPointerCapture(e.pointerId);
        e.preventDefault();
    };
    const onMove = (e) => {
        if (!dragging) {
            return;
        }
        const container = left.parentElement;
        const rect = container.getBoundingClientRect();
        let width = e.clientX - rect.left;
        width = Math.max(min, Math.min(rect.width - min, width));
        left.style.flex = '0 0 ' + width + 'px';
    };
    const onUp = () => { dragging = false; };

    handle.addEventListener('pointerdown', onDown);
    window.addEventListener('pointermove', onMove);
    window.addEventListener('pointerup', onUp);

    return {
        dispose() {
            handle.removeEventListener('pointerdown', onDown);
            window.removeEventListener('pointermove', onMove);
            window.removeEventListener('pointerup', onUp);
        }
    };
}
