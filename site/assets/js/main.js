/* STAKEOUT front page.
 * No dependencies. Text goes into the DOM only through textContent, never innerHTML,
 * so the page runs under a strict CSP with Trusted Types enforced. */
(() => {
  'use strict';

  const doc = document;
  const root = doc.documentElement;
  const $ = (sel, ctx = doc) => ctx.querySelector(sel);
  const $$ = (sel, ctx = doc) => Array.from(ctx.querySelectorAll(sel));
  const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
  const clamp = (v, lo, hi) => (v < lo ? lo : v > hi ? hi : v);
  const smooth = (e0, e1, x) => {
    const t = clamp((x - e0) / (e1 - e0), 0, 1);
    return t * t * (3 - 2 * t);
  };

  /* ---------- DTG clock (military date-time group, UTC) ---------- */

  const MONTHS = ['JAN', 'FEB', 'MAR', 'APR', 'MAY', 'JUN', 'JUL', 'AUG', 'SEP', 'OCT', 'NOV', 'DEC'];
  const pad = (n) => String(n).padStart(2, '0');

  function tickDtg() {
    const d = new Date();
    const s = pad(d.getUTCDate()) + pad(d.getUTCHours()) + pad(d.getUTCMinutes()) + 'Z' +
      MONTHS[d.getUTCMonth()] + String(d.getUTCFullYear()).slice(-2);
    $$('.js-dtg').forEach((el) => { el.textContent = s; });
  }

  /* ---------- logo geometry ---------- */

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

  /* ---------- live background: halftone smoke in a WebGL fragment shader ---------- */

  const VERT = 'attribute vec2 p;void main(){gl_Position=vec4(p,0.,1.);}';
  const FRAG = [
    'precision mediump float;',
    'uniform vec2 uRes;uniform float uTime;uniform vec2 uHeat;uniform float uCell;',
    'float hash(vec2 p){p=fract(p*vec2(123.34,456.21));p+=dot(p,p+45.32);return fract(p.x*p.y);}',
    'float noise(vec2 p){vec2 i=floor(p),f=fract(p);vec2 u=f*f*(3.-2.*f);',
    'return mix(mix(hash(i),hash(i+vec2(1.,0.)),u.x),mix(hash(i+vec2(0.,1.)),hash(i+vec2(1.,1.)),u.x),u.y);}',
    'float fbm(vec2 p){float v=0.,a=.5;for(int i=0;i<4;i++){v+=a*noise(p);p=p*2.03+vec2(1.7,9.2);a*=.5;}return v;}',
    'void main(){',
    '  float c=.8660254,s=.5;',
    '  vec2 g=mat2(c,-s,s,c)*gl_FragCoord.xy/uCell;',
    '  vec2 id=floor(g)+.5;',
    '  vec2 cp=mat2(c,s,-s,c)*(id*uCell);',
    '  vec2 uv=cp/uRes.y;',
    '  float t=uTime*.035;',
    '  vec2 q=vec2(fbm(uv*1.5+t),fbm(uv*1.5-t+3.1));',
    '  float f=fbm(uv*1.7+1.6*q+vec2(t*.7,-t*.4));',
    '  float tone=smoothstep(.3,.8,f);',
    '  float heat=smoothstep(uRes.y*.42,0.,distance(cp,uHeat));',
    '  float r=.5*(tone*.72+heat*.42);',
    '  float d=length(g-id);',
    '  float aa=1./uCell;',
    '  float dotm=1.-smoothstep(r-aa,r+aa,d);',
    '  vec3 bg=vec3(.043,.043,.039);',
    '  vec3 ink=mix(vec3(.93,.90,.85)*.24,vec3(1.,.29,.11)*.92,smoothstep(.05,.7,heat));',
    '  gl_FragColor=vec4(mix(bg,ink,dotm),1.);',
    '}',
  ].join('\n');

  function initBackground() {
    const canvas = $('#bg');
    if (!canvas) return;
    let gl = null;
    try {
      gl = canvas.getContext('webgl', {
        alpha: false, antialias: false, depth: false, stencil: false,
        premultipliedAlpha: false, preserveDrawingBuffer: false, powerPreference: 'low-power',
      });
    } catch (_) {
      gl = null;
    }
    if (!gl) return; // CSS dot pattern on .bg stays as the fallback.

    const compile = (type, src) => {
      const sh = gl.createShader(type);
      gl.shaderSource(sh, src);
      gl.compileShader(sh);
      return gl.getShaderParameter(sh, gl.COMPILE_STATUS) ? sh : null;
    };
    const vs = compile(gl.VERTEX_SHADER, VERT);
    const fs = compile(gl.FRAGMENT_SHADER, FRAG);
    if (!vs || !fs) return;
    const prog = gl.createProgram();
    gl.attachShader(prog, vs);
    gl.attachShader(prog, fs);
    gl.linkProgram(prog);
    if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) return;
    gl.useProgram(prog);

    const buf = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, buf);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 3, -1, -1, 3]), gl.STATIC_DRAW);
    const loc = gl.getAttribLocation(prog, 'p');
    gl.enableVertexAttribArray(loc);
    gl.vertexAttribPointer(loc, 2, gl.FLOAT, false, 0, 0);
    const uRes = gl.getUniformLocation(prog, 'uRes');
    const uTime = gl.getUniformLocation(prog, 'uTime');
    const uHeat = gl.getUniformLocation(prog, 'uHeat');
    const uCell = gl.getUniformLocation(prog, 'uCell');

    let dpr = 1;
    let W = 0;
    let H = 0;
    const resize = () => {
      dpr = Math.min(window.devicePixelRatio || 1, 1.5);
      W = Math.max(1, Math.round(window.innerWidth * dpr));
      H = Math.max(1, Math.round(window.innerHeight * dpr));
      canvas.width = W;
      canvas.height = H;
      gl.viewport(0, 0, W, H);
    };
    resize();

    // Heat spot follows the pointer; with no pointer it drifts on its own.
    const heat = { x: W * 0.7, y: H * 0.6 };
    const aim = { x: heat.x, y: heat.y };
    let lastPointer = -1e9;
    window.addEventListener('pointermove', (e) => {
      if (e.pointerType !== 'mouse') return;
      aim.x = e.clientX * dpr;
      aim.y = (window.innerHeight - e.clientY) * dpr;
      lastPointer = performance.now();
    }, { passive: true });

    const draw = (now) => {
      gl.uniform2f(uRes, W, H);
      gl.uniform1f(uTime, now / 1000);
      gl.uniform2f(uHeat, heat.x, heat.y);
      gl.uniform1f(uCell, 7 * dpr);
      gl.drawArrays(gl.TRIANGLES, 0, 3);
    };

    let raf = 0;
    let last = 0;
    let lost = false;
    const FRAME = 1000 / 30; // slow smoke does not need 60 fps
    const loop = (now) => {
      raf = requestAnimationFrame(loop);
      const dt = now - last;
      if (dt < FRAME) return;
      last = now;
      if (now - lastPointer > 2500) {
        const t = now / 1000;
        aim.x = W * (0.5 + 0.34 * Math.cos(t * 0.13));
        aim.y = H * (0.5 + 0.3 * Math.sin(t * 0.19));
      }
      const a = 1 - Math.exp(-Math.min(dt, 100) / 260);
      heat.x += (aim.x - heat.x) * a;
      heat.y += (aim.y - heat.y) * a;
      draw(now);
    };

    const start = () => {
      cancelAnimationFrame(raf);
      if (lost) return;
      if (reduceMotion.matches) draw(4000);
      else raf = requestAnimationFrame(loop);
    };

    let rt = 0;
    window.addEventListener('resize', () => {
      clearTimeout(rt);
      rt = setTimeout(() => { resize(); if (reduceMotion.matches) draw(4000); }, 120);
    }, { passive: true });
    reduceMotion.addEventListener?.('change', start);
    canvas.addEventListener('webglcontextlost', (e) => { e.preventDefault(); lost = true; cancelAnimationFrame(raf); });
    canvas.addEventListener('webglcontextrestored', () => { lost = false; initBackground(); });
    start();
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
    let t = 0;
    window.addEventListener('resize', () => { cancelAnimationFrame(t); t = requestAnimationFrame(fit); }, { passive: true });
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
      card.addEventListener('pointerleave', () => { x = 0; y = 0; if (!raf) raf = requestAnimationFrame(apply); });
    });
  }

  /* ---------- section pages (hash routes) ---------- */

  const ROUTES = {
    stories: {
      tag: 'операции', sub: 'Операции по минутам', title: 'Рассказы',
      lead: 'Хронология, участники, решения и их цена. В тексте только то, что подтверждают документы и показания участников.',
      items: [
        ['Такур-Гар', 'Афганистан, март 2002'],
        ['Тонго-Тонго', 'Нигер, октябрь 2017'],
        ['Нептун Спир', 'Пакистан, май 2011'],
      ],
    },
    gear: {
      tag: 'сборки', sub: 'Снаряжение по эпохам', title: 'Сборки снаряжения',
      lead: 'Комплекты по операциям и периодам: что носили, зачем и как это менялось.',
      items: [
        ['Плитник и разгрузка', 'Категория'],
        ['Шлем и навесное', 'Категория'],
        ['Связь и оптика', 'Категория'],
      ],
    },
    units: {
      tag: 'досье', sub: 'Страницы подразделений', title: 'Подразделения',
      lead: 'История, отбор, структура и ключевые операции каждого подразделения.',
      items: [
        ['DEVGRU', 'США'],
        ['SAS', 'Великобритания'],
        ['GROM', 'Польша'],
        ['KSK', 'Германия'],
      ],
    },
    guides: {
      tag: 'практика', sub: 'Пошаговые разборы', title: 'Гайды',
      lead: 'Как проверять источники, читать наградные документы и отличать подтверждённое от слухов.',
      items: [
        ['Как читать наградное представление', 'Источники'],
        ['Где искать первоисточники по операции', 'Методика'],
        ['Как датировать снаряжение на фото', 'Проверка'],
      ],
    },
  };
  const SITE = 'STAKEOUT';

  const routeFromHash = () => {
    const h = window.location.hash.slice(1);
    return Object.prototype.hasOwnProperty.call(ROUTES, h) ? h : 'home';
  };

  function render(route) {
    const home = $('#view-home');
    const page = $('#view-page');
    if (!home || !page) return;
    $$('.topnav a').forEach((a) => {
      if (a.getAttribute('href') === '#' + route) a.setAttribute('aria-current', 'page');
      else a.removeAttribute('aria-current');
    });
    if (route === 'home') {
      page.hidden = true;
      home.hidden = false;
      doc.title = SITE;
      return;
    }
    const r = ROUTES[route];
    $('#page-tag').textContent = r.tag;
    $('#page-sub').textContent = r.sub;
    $('#page-title').textContent = r.title;
    $('#page-lead').textContent = r.lead;
    const list = $('#page-list');
    list.replaceChildren(...r.items.map(([name, info]) => {
      const li = doc.createElement('li');
      const ex = doc.createElement('span');
      ex.className = 'ex';
      ex.textContent = 'пример';
      const nm = doc.createElement('span');
      nm.className = 'name';
      nm.textContent = name;
      const inf = doc.createElement('span');
      inf.className = 'info mono';
      inf.textContent = info;
      li.append(ex, nm, inf);
      return li;
    }));
    home.hidden = true;
    page.hidden = false;
    doc.title = r.title + ' · ' + SITE;
    const h1 = $('#page-title');
    h1.tabIndex = -1;
    h1.focus({ preventScroll: true });
  }

  function initRouter() {
    const wipe = $('#wipe');
    let busy = 0;
    const go = () => {
      const route = routeFromHash();
      if (reduceMotion.matches || !wipe) {
        render(route);
        window.scrollTo(0, 0);
        return;
      }
      clearTimeout(busy);
      wipe.classList.remove('is-out');
      void wipe.offsetWidth; // restart the transition from the left edge
      wipe.classList.add('is-in');
      busy = setTimeout(() => {
        render(route);
        window.scrollTo(0, 0);
        wipe.classList.remove('is-in');
        wipe.classList.add('is-out');
        busy = setTimeout(() => wipe.classList.remove('is-out'), 650);
      }, 580);
    };
    window.addEventListener('hashchange', go);
    render(routeFromHash());
  }

  /* ---------- loader ---------- */

  function runLoader() {
    const el = $('#loader');
    const ready = () => root.classList.add('is-ready');
    if (!el) { ready(); return; }
    root.classList.add('is-locked');
    const t0 = performance.now();
    const MIN = reduceMotion.matches ? 350 : 2300;
    const MAX = 4500;
    const count = $('#loader-count');
    let fontsReady = !doc.fonts;
    let done = false;
    if (doc.fonts && doc.fonts.ready) doc.fonts.ready.then(() => { fontsReady = true; });

    const finish = () => {
      if (done) return;
      done = true;
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

  /* ---------- boot ---------- */

  function boot() {
    tickDtg();
    setInterval(tickDtg, 15000);
    runLoader();
    initHero();
    initRouter();
    initBackground();
    initCards();
    initHalftones();
  }

  if (doc.readyState === 'loading') doc.addEventListener('DOMContentLoaded', boot, { once: true });
  else boot();
})();
