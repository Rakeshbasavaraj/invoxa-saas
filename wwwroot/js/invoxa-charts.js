// Minimal, dependency-free charts for Invoxa Dashboard.
// Avoids external CDN dependencies so charts work offline/intranet.
(function () {
  const COLORS = [
    '#16a34a', // green
    '#f59e0b', // amber
    '#dc2626', // red
    '#2563eb', // blue
    '#6b7280', // gray
    '#7c3aed'  // purple
  ];

  function toNumber(v) {
    const n = Number(v);
    return Number.isFinite(n) ? n : 0;
  }

  function clearCanvas(c) {
    const ctx = c.getContext('2d');
    const dpr = window.devicePixelRatio || 1;
    // Some browsers can report 0px canvas size if the element isn't fully laid out yet.
    // Fall back to computed style / attribute height so we always render.
    const rect = c.getBoundingClientRect();
    let w = rect.width;
    let h = rect.height;
    if (!w || !h) {
      const cs = window.getComputedStyle(c);
      w = w || parseFloat(cs.width) || c.parentElement?.getBoundingClientRect()?.width || 600;
      h = h || parseFloat(cs.height) || Number(c.getAttribute('height')) || 260;
    }

    c.width = Math.max(1, Math.floor(w * dpr));
    c.height = Math.max(1, Math.floor(h * dpr));

    // Reset transform before scaling (prevents cumulative scaling on redraw).
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.scale(dpr, dpr);
    ctx.clearRect(0, 0, w, h);
    return { ctx, w, h };
  }


  function roundRect(ctx, x, y, w, h, r, fill, stroke) {
    const radius = Math.min(r, w / 2, h / 2);
    ctx.beginPath();
    ctx.moveTo(x + radius, y);
    ctx.arcTo(x + w, y, x + w, y + h, radius);
    ctx.arcTo(x + w, y + h, x, y + h, radius);
    ctx.arcTo(x, y + h, x, y, radius);
    ctx.arcTo(x, y, x + w, y, radius);
    ctx.closePath();
    if (fill) ctx.fill();
    if (stroke) ctx.stroke();
  }

  function drawLineChart(canvasId, labels, values, label) {
    const c = document.getElementById(canvasId);
    if (!c) return;

    const vals = (values || []).map(toNumber);
    const labs = (labels || []).map(String);

    function render(activeIndex) {
      const { ctx, w, h } = clearCanvas(c);
      const padding = 28;
      const x0 = padding;
      const y0 = h - padding;
      const x1 = w - padding;
      const y1 = padding;

      const maxV = Math.max(1, ...vals);
      const minV = Math.min(0, ...vals);
      const range = Math.max(1, maxV - minV);

      // Grid
      ctx.lineWidth = 1;
      ctx.strokeStyle = 'rgba(17,24,39,0.08)';
      const gridLines = 4;
      for (let i = 0; i <= gridLines; i++) {
        const y = y1 + (i * (y0 - y1) / gridLines);
        ctx.beginPath();
        ctx.moveTo(x0, y);
        ctx.lineTo(x1, y);
        ctx.stroke();
      }

      // Axes
      ctx.strokeStyle = 'rgba(17,24,39,0.18)';
      ctx.beginPath();
      ctx.moveTo(x0, y1);
      ctx.lineTo(x0, y0);
      ctx.lineTo(x1, y0);
      ctx.stroke();

      const n = Math.max(1, vals.length);
      const stepX = n > 1 ? (x1 - x0) / (n - 1) : 0;
      const pts = [];
      for (let i = 0; i < n; i++) {
        const v = vals[i] ?? 0;
        const x = x0 + i * stepX;
        const y = y0 - ((v - minV) / range) * (y0 - y1);
        pts.push({ x, y, value: v, label: labs[i] ?? '' });
      }

      if (!pts.length) return;

      const grad = ctx.createLinearGradient(0, y1, 0, y0);
      grad.addColorStop(0, 'rgba(37,99,235,0.22)');
      grad.addColorStop(1, 'rgba(37,99,235,0.00)');
      ctx.fillStyle = grad;
      ctx.beginPath();
      ctx.moveTo(pts[0].x, y0);
      pts.forEach((p) => ctx.lineTo(p.x, p.y));
      ctx.lineTo(pts[pts.length - 1].x, y0);
      ctx.closePath();
      ctx.fill();

      ctx.save();
      ctx.lineWidth = 2.5;
      ctx.strokeStyle = '#2563eb';
      ctx.shadowColor = 'rgba(37,99,235,0.25)';
      ctx.shadowBlur = 8;
      ctx.beginPath();
      pts.forEach((p, idx) => {
        if (idx === 0) ctx.moveTo(p.x, p.y);
        else ctx.lineTo(p.x, p.y);
      });
      ctx.stroke();
      ctx.restore();

      pts.forEach((p, index) => {
        const isActive = index === activeIndex;
        ctx.beginPath();
        ctx.fillStyle = '#2563eb';
        ctx.arc(p.x, p.y, isActive ? 5 : 3.5, 0, Math.PI * 2);
        ctx.fill();
        ctx.strokeStyle = '#ffffff';
        ctx.lineWidth = 2;
        ctx.stroke();
      });

      if (vals.every(v => v === 0)) {
        ctx.fillStyle = 'rgba(107,114,128,0.85)';
        ctx.font = '12px system-ui, -apple-system, Segoe UI, Roboto, Arial';
        ctx.fillText('No paid invoices in this period', x0, y1 + 18);
      }

      if (activeIndex !== null && activeIndex >= 0 && activeIndex < pts.length) {
        const p = pts[activeIndex];

        ctx.save();
        ctx.strokeStyle = 'rgba(37,99,235,0.18)';
        ctx.setLineDash([4, 4]);
        ctx.beginPath();
        ctx.moveTo(p.x, y1);
        ctx.lineTo(p.x, y0);
        ctx.stroke();
        ctx.restore();

        const title = p.label;
        const amount = `${label || 'Value'}: ${p.value.toFixed(2)}`;
        ctx.font = '600 12px system-ui, -apple-system, Segoe UI, Roboto, Arial';
        const titleWidth = ctx.measureText(title).width;
        ctx.font = '500 12px system-ui, -apple-system, Segoe UI, Roboto, Arial';
        const amountWidth = ctx.measureText(amount).width;
        const boxW = Math.max(titleWidth, amountWidth) + 24;
        const boxH = 50;
        let boxX = p.x - boxW / 2;
        let boxY = p.y - boxH - 14;
        if (boxX < 10) boxX = 10;
        if (boxX + boxW > w - 10) boxX = w - boxW - 10;
        if (boxY < 10) boxY = p.y + 14;

        ctx.save();
        ctx.fillStyle = 'rgba(15,23,42,0.96)';
        ctx.strokeStyle = 'rgba(255,255,255,0.10)';
        roundRect(ctx, boxX, boxY, boxW, boxH, 12, true, true);
        ctx.fillStyle = '#ffffff';
        ctx.font = '600 12px system-ui, -apple-system, Segoe UI, Roboto, Arial';
        ctx.fillText(title, boxX + 12, boxY + 18);
        ctx.font = '500 12px system-ui, -apple-system, Segoe UI, Roboto, Arial';
        ctx.fillStyle = 'rgba(255,255,255,0.88)';
        ctx.fillText(amount, boxX + 12, boxY + 36);
        ctx.restore();
      }
    }

    function getActiveIndex(evt) {
      if (!vals.length) return null;
      const rect = c.getBoundingClientRect();
      const x = evt.clientX - rect.left;
      const y = evt.clientY - rect.top;
      const padding = 28;
      const x0 = padding;
      const x1 = rect.width - padding;
      const y0 = rect.height - padding;
      const y1 = padding;
      if (x < x0 - 12 || x > x1 + 12 || y < y1 - 12 || y > y0 + 12) return null;
      if (vals.length === 1) return 0;
      const stepX = (x1 - x0) / (vals.length - 1);
      const idx = Math.round((x - x0) / stepX);
      return Math.max(0, Math.min(vals.length - 1, idx));
    }

    c.onmousemove = (evt) => {
      c.style.cursor = 'crosshair';
      render(getActiveIndex(evt));
    };
    c.onmouseleave = () => {
      c.style.cursor = 'default';
      render(null);
    };

    render(null);
  }

  function drawDoughnutChart(canvasId, labels, values) {
    const c = document.getElementById(canvasId);
    if (!c) return;

    const { ctx, w, h } = clearCanvas(c);
    const cx = w / 2;
    const cy = h / 2;
    const radius = Math.min(w, h) * 0.34;
    const inner = radius * 0.45; // closer to pie style

    const vals = (values || []).map(toNumber);
    const total = vals.reduce((a, b) => a + b, 0);

    // Empty state
    if (!total) {
      ctx.fillStyle = 'rgba(107,114,128,0.85)';
      ctx.font = '12px system-ui, -apple-system, Segoe UI, Roboto, Arial';
      ctx.fillText('No invoices yet', 12, 18);
      // draw light ring
      ctx.strokeStyle = 'rgba(17,24,39,0.10)';
      ctx.lineWidth = radius - inner;
      ctx.beginPath();
      ctx.arc(cx, cy, (radius + inner) / 2, 0, Math.PI * 2);
      ctx.stroke();
      return;
    }

    let start = -Math.PI / 2;
    for (let i = 0; i < vals.length; i++) {
      const v = vals[i];
      const slice = (v / total) * Math.PI * 2;
      const end = start + slice;

      ctx.beginPath();
      ctx.moveTo(cx, cy);
      ctx.arc(cx, cy, radius, start, end);
      ctx.closePath();
      ctx.fillStyle = COLORS[i % COLORS.length];
      ctx.fill();

      start = end;
    }

    // Inner cutout
    ctx.globalCompositeOperation = 'destination-out';
    ctx.beginPath();
    ctx.arc(cx, cy, inner, 0, Math.PI * 2);
    ctx.fill();
    ctx.globalCompositeOperation = 'source-over';

    // Center text
    ctx.fillStyle = 'rgba(17,24,39,0.75)';
    ctx.font = '12px system-ui, -apple-system, Segoe UI, Roboto, Arial';
    ctx.textAlign = 'center';
    ctx.fillText('Total', cx, cy - 2);
    ctx.font = '800 16px system-ui, -apple-system, Segoe UI, Roboto, Arial';
    ctx.fillText(String(total), cx, cy + 18);

    // Legend (right)
    ctx.textAlign = 'left';
    ctx.font = '12px system-ui, -apple-system, Segoe UI, Roboto, Arial';
    const lx = w - 170;
    let ly = 22;
    for (let i = 0; i < labels.length; i++) {
      const name = String(labels[i] ?? '');
      const v = vals[i] ?? 0;
      const pct = total ? Math.round((v / total) * 100) : 0;
      ctx.fillStyle = COLORS[i % COLORS.length];
      ctx.fillRect(lx, ly - 10, 10, 10);
      ctx.fillStyle = 'rgba(17,24,39,0.75)';
      ctx.fillText(`${name}`, lx + 16, ly - 1);
      ctx.fillStyle = 'rgba(107,114,128,0.85)';
      ctx.fillText(`${pct}%`, lx + 110, ly - 1);
      ly += 20;
      if (ly > h - 10) break;
    }
  }

  
  function drawBarChart(canvasId, labels, values, valueLabel) {
    const c = document.getElementById(canvasId);
    if (!c) return;

    const { ctx, w, h } = clearCanvas(c);
    const padding = 18;
    const x0 = padding;
    const y0 = padding;
    const x1 = w - padding;
    const y1 = h - padding;

    const labs = (labels || []).map(String);
    const vals = (values || []).map(toNumber);

    if (labs.length === 0) {
      ctx.fillStyle = 'rgba(107,114,128,0.85)';
      ctx.fillText('No paid invoices yet', x0, y0 + 18);
      return;
    }

    const maxV = Math.max(1, ...vals);
    const rowH = Math.max(28, Math.floor((y1 - y0) / labs.length));
    const barH = Math.max(10, Math.floor(rowH * 0.45));
    const labelW = Math.min(220, Math.floor(w * 0.40));

    // Title-like axis hint
    ctx.fillStyle = 'rgba(107,114,128,0.85)';
    ctx.fillText(valueLabel || '', x1 - 60, y0 + 12);

    for (let i = 0; i < labs.length; i++) {
      const y = y0 + i * rowH + 22;
      const v = vals[i] ?? 0;

      // label
      ctx.fillStyle = 'rgba(15,23,42,0.90)';
      ctx.font = '13px system-ui, -apple-system, Segoe UI, Roboto, Ubuntu, Arial, sans-serif';
      const label = labs[i].length > 26 ? labs[i].slice(0, 25) + '…' : labs[i];
      ctx.fillText(label, x0, y);

      // bar background
      const bx = x0 + labelW;
      const bw = (x1 - bx);
      const by = y - 10;
      ctx.fillStyle = 'rgba(17,24,39,0.06)';
      roundRect(ctx, bx, by, bw, barH, 10, true, false);

      // bar
      const fillW = Math.max(2, Math.floor((v / maxV) * (bw)));
      ctx.fillStyle = COLORS[(i + 3) % COLORS.length];
      roundRect(ctx, bx, by, fillW, barH, 10, true, false);

      // value text
      ctx.fillStyle = 'rgba(15,23,42,0.75)';
      ctx.fillText(`${v.toFixed(2)}`, bx + bw - 60, y);
    }
  }

window.InvoxaCharts = {
    drawLineChart,
    drawDoughnutChart,
    drawBarChart
  };
})();
