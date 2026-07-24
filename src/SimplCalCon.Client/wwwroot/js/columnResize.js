// Drag-to-resize table columns (ADR 0036). Adds a grip to each header cell's right edge; the
// table uses fixed layout + min-width:100%, so widening a column grows the table (the list
// pane scrolls) and the initial fit has no horizontal scrollbar.
export function init(table) {
    const headerCells = Array.from(table.querySelectorAll('thead tr:first-child th'));

    // Freeze current widths so resizing is stable, then let the table grow past the pane.
    headerCells.forEach(th => { th.style.width = th.offsetWidth + 'px'; });
    table.style.tableLayout = 'fixed';
    table.style.width = 'auto';
    table.style.minWidth = '100%';

    const grips = [];
    headerCells.forEach((th, index) => {
        if (index === headerCells.length - 1) {
            return; // last column just takes the remainder
        }

        const grip = document.createElement('div');
        grip.className = 'col-grip';
        th.appendChild(grip);
        grips.push(grip);

        let startX = 0;
        let startWidth = 0;
        const onMove = (e) => { th.style.width = Math.max(48, startWidth + (e.clientX - startX)) + 'px'; };
        const onUp = () => {
            window.removeEventListener('pointermove', onMove);
            window.removeEventListener('pointerup', onUp);
        };

        grip.addEventListener('pointerdown', (e) => {
            startX = e.clientX;
            startWidth = th.offsetWidth;
            e.preventDefault();
            e.stopPropagation();
            window.addEventListener('pointermove', onMove);
            window.addEventListener('pointerup', onUp);
        });
        // Don't let a grip click reach the header's sort handler.
        grip.addEventListener('click', (e) => { e.stopPropagation(); e.preventDefault(); });
    });

    return { dispose() { grips.forEach(g => g.remove()); } };
}
