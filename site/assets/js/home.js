/* STAKEOUT start screen: logo loader, hero wordmark, halftone card art, card parallax.
 * Depends on core.js (window.STK). Writes text only through textContent. */
(() => {
  'use strict';

  const STK = window.STK;
  if (!STK) return;
  const { $, $$, clamp, reduceMotion, store } = STK;
  const doc = document;
  const root = doc.documentElement;
  const smooth = (e0, e1, x) => {
    const t = clamp((x - e0) / (e1 - e0), 0, 1);
    return t * t * (3 - 2 * t);
  };

  /* ---------- logo geometry (inline <defs> on this page only) ---------- */

  function logoPaths() {
    const sil = $('#logo-sil');
    const ink = $('#logo-ink');
    if (!sil || !ink || typeof Path2D !== 'function') return null;
    return { sil: new Path2D(sil.getAttribute('d')), ink: new Path2D(ink.getAttribute('d')) };
  }

  /* ---------- halftone renderer (2D canvas, redrawn only on resize) ---------- */

  // crop: region of the 640x910 logo; place: target box as fractions of the canvas.
  const HALFTONE = {
    stories: {
      crop: [150, 150, 380, 500], place: [0.02, -0.06, 0.46, 1.12], rot: -0.1,
      bg: '#0b0b0a', dot: '#7f796e', cell: 5.5, angle: Math.PI / 4, blur: 3,
      gain: 1, floor: 0, invert: false,
      fade: (x, y) => smooth(0.6, 0.28, x) * (0.45 + 0.55 * smooth(1, 0.15, y)),
    },
    'units-a': {
      crop: [196, 300, 258, 210], place: [0, 0, 1, 1], rot: 0,
      bg: '#ff4a1c', dot: '#1a0802', cell: 4.2, angle: Math.PI / 6, blur: 1.6,
      gain: 0.95, floor: 0.04, invert: true,
    },
    'units-b': {
      crop: [224, 470, 205, 175], place: [0, 0, 1, 1], rot: 0,
      bg: '#ff4a1c', dot: '#1a0802', cell: 4.2, angle: Math.PI / 6, blur: 1.6,
      gain: 0.95, floor: 0.04, invert: true,
    },
    guides: {
      crop: [0, 320, 640, 420], place: [-0.08, -0.35, 1.16, 1.7], rot: -0.22,
      bg: '#ff4a1c', dot: '#1a0802', cell: 7, angle: Math.PI / 12, blur: 4,
      gain: 0.9, floor: 0.07, invert: false,
    },
  };

  function renderHalftone(canvas, spec, paths) {
    const w = canvas.clientWidth;
    const h = canvas.clientHeight;
    if (!w || !h) return;
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const key = w + 'x' + h + '@' + dpr;
    if (canvas.dataset.size === key) return;
    canvas.dataset.size = key;
    canvas.width = Math.round(w * dpr);
    canvas.height = Math.round(h * dpr);
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    // 1. Tone map: draw the logo into a small offscreen canvas, read luminance.
    const k = 0.5;
    const sw = Math.max(1, Math.round(w * k));
    const sh = Math.max(1, Math.round(h * k));
    const off = doc.createElement('canvas');
    off.width = sw;
    off.height = sh;
    const o = off.getContext('2d', { willReadFrequently: true });
    if (!o) return;
    o.fillStyle = '#000';
    o.fillRect(0, 0, sw, sh);
    const [cx, cy, cw, ch] = spec.crop;
    const [px, py, pw, ph] = spec.place;
    const dw = pw * w;
    const dh = ph * h;
    const s = Math.max(dw / cw, dh / ch);
    o.save();
    if ('filter' in o) o.filter = 'blur(' + (spec.blur * k).toFixed(2) + 'px)';
    o.translate((px * w + dw / 2) * k, (py * h + dh / 2) * k);
    o.rotate(spec.rot);
    o.scale(s * k, s * k);
    o.translate(-(cx + cw / 2), -(cy + ch / 2));
    o.fillStyle = '#fff';
    o.fill(paths.sil);
    o.fillStyle = '#000';
    o.fill(paths.ink);
    o.restore();
    let data;
    try {
      data = o.getImageData(0, 0, sw, sh).data;
    } catch (_) {
      return;
    }

    // 2. Dots on a rotated grid; radius follows the square root so dot area tracks tone.
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.fillStyle = spec.bg;
    ctx.fillRect(0, 0, w, h);
    ctx.fillStyle = spec.dot;
    const cell = spec.cell;
    const ca = Math.cos(spec.angle);
    const sa = Math.sin(spec.angle);
    const n = Math.ceil(Math.hypot(w, h) / cell / 2) + 1;
    const maxR = cell * 0.62;
    ctx.beginPath();
    for (let j = -n; j <= n; j++) {
      for (let i = -n; i <= n; i++) {
        const gx = i * cell;
        const gy = j * cell;
        const x = w / 2 + gx * ca - gy * sa;
        const y = h / 2 + gx * sa + gy * ca;
        if (x < -cell || y < -cell || x > w + cell || y > h + cell) continue;
        const sx = clamp((x * k) | 0, 0, sw - 1);
        const sy = clamp((y * k) | 0, 0, sh - 1);
        let t = data[(sy * sw + sx) * 4] / 255;
        if (spec.invert) t = 1 - t;
        t = spec.floor + t * spec.gain;
        if (spec.fade) t *= spec.fade(x / w, y / h);
        const r = maxR * Math.sqrt(clamp(t, 0, 1));
        if (r < 0.3) continue;
        ctx.moveTo(x + r, y);
        ctx.arc(x, y, r, 0, Math.PI * 2);
      }
    }
    ctx.fill();
  }

  function initHalftones() {
    const paths = logoPaths();
    if (!paths) return;
    const canvases = $$('canvas[data-ht]');
    const draw = () => canvases.forEach((c) => {
      const spec = HALFTONE[c.dataset.ht];
      if (spec) renderHalftone(c, spec, paths);
    });
    draw();
    let timer = 0;
    window.addEventListener('resize', () => {
      clearTimeout(timer);
      timer = setTimeout(draw, 160);
    }, { passive: true });
  }

  /* ---------- hero wordmark: split into letters, fit to the column ---------- */

  function initHero() {
    const title = $('#hero-title');
    if (!title) return;
    const word = title.textContent.trim();
    title.textContent = '';
    for (const ch of word) {
      const span = doc.createElement('span');
      span.className = 'ch';
      span.setAttribute('aria-hidden', 'true');
      span.textContent = ch;
      title.appendChild(span);
    }
    const fit = () => {
      const box = title.parentElement;
      const cs = getComputedStyle(box);
      const avail = box.clientWidth - parseFloat(cs.paddingLeft) - parseFloat(cs.paddingRight);
      title.style.fontSize = '100px';
      const wNow = title.getBoundingClientRect().width;
      // 0.97 leaves room for the last glyph's overhang past its advance width.
      if (wNow > 0) title.style.fontSize = Math.min(260, (97 * avail) / wNow).toFixed(2) + 'px';
    };
    fit();
    if (doc.fonts && doc.fonts.ready) doc.fonts.ready.then(fit);
    // Font faces can finish after 'ready' resolved (e.g. a late unicode-range subset).
    if (doc.fonts && doc.fonts.addEventListener) doc.fonts.addEventListener('loadingdone', fit);
    let t = 0;
    window.addEventListener('resize', () => {
      cancelAnimationFrame(t);
      t = requestAnimationFrame(fit);
    }, { passive: true });
  }

  /* ---------- card pointer parallax ---------- */

  function initCards() {
    if (!window.matchMedia('(hover: hover)').matches) return;
    $$('.card').forEach((card) => {
      let raf = 0;
      let x = 0;
      let y = 0;
      const apply = () => {
        raf = 0;
        card.style.setProperty('--px', x.toFixed(3));
        card.style.setProperty('--py', y.toFixed(3));
      };
      card.addEventListener('pointermove', (e) => {
        const r = card.getBoundingClientRect();
        x = ((e.clientX - r.left) / r.width) * 2 - 1;
        y = ((e.clientY - r.top) / r.height) * 2 - 1;
        if (!raf) raf = requestAnimationFrame(apply);
      }, { passive: true });
      card.addEventListener('pointerleave', () => {
        x = 0;
        y = 0;
        if (!raf) raf = requestAnimationFrame(apply);
      });
    });
  }

  /* ---------- loader: once per browser session ---------- */

  function runLoader() {
    const el = $('#loader');
    const ready = () => root.classList.add('is-ready');
    if (!el) { ready(); return; }
    if (root.classList.contains('intro-seen')) {
      // Already played in this session; CSS hid it before the first paint.
      el.remove();
      ready();
      return;
    }
    root.classList.add('is-locked');
    const t0 = performance.now();
    const MIN = reduceMotion.matches ? 350 : 2300;
    const MAX = 4500;
    const count = $('#loader-count');
    const status = $('#loader-status');
    let fontsReady = !doc.fonts;
    let done = false;
    if (doc.fonts && doc.fonts.ready) doc.fonts.ready.then(() => { fontsReady = true; });

    // Everything behind the overlay is out of reach until it leaves: Tab stays on «Пропустить».
    const behind = Array.from(doc.body.children).filter((n) => n !== el && n !== status && n.tagName !== 'SCRIPT');
    behind.forEach((n) => { n.inert = true; });
    // One polite announcement instead of a live region that changes every frame.
    if (status) status.textContent = 'Загрузка STAKEOUT';

    const finish = () => {
      if (done) return;
      done = true;
      store.set('stk:intro', '1');
      behind.forEach((n) => { n.inert = false; });
      if (status) status.textContent = '';
      if (count) count.textContent = '100';
      const quick = reduceMotion.matches;
      if (!quick) el.classList.add('is-scanned');
      setTimeout(() => {
        el.classList.add('is-leaving');
        root.classList.remove('is-locked');
        setTimeout(ready, quick ? 0 : 280);
        setTimeout(() => el.remove(), quick ? 60 : 1150);
      }, quick ? 0 : 360);
    };

    const step = (now) => {
      if (done) return;
      const t = now - t0;
      const p = Math.min(1, t / MIN) * (fontsReady ? 1 : 0.94);
      if (count) count.textContent = String(Math.round(p * 100)).padStart(3, '0');
      if ((t >= MIN && fontsReady) || t >= MAX) finish();
      else requestAnimationFrame(step);
    };
    requestAnimationFrame(step);
    setTimeout(finish, MAX + 200); // rAF pauses in background tabs
    const skip = $('#loader-skip');
    if (skip) skip.addEventListener('click', finish);
    window.addEventListener('keydown', (e) => { if (e.key === 'Escape') finish(); });
  }

  runLoader();
  initHero();
  initCards();
  initHalftones();
})();
