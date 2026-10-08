/* STAKEOUT core, shared by every page: DTG clock, header metrics, smoke background.
 * No dependencies. Text reaches the DOM only through textContent and nothing parses
 * HTML, so the page runs under CSP with Trusted Types enforced ('trusted-types none').
 * Exposes a small frozen helper namespace, window.STK, for page scripts. */
(() => {
  'use strict';

  const doc = document;
  const root = doc.documentElement;
  const boot = window.STK_BOOT || Object.freeze({ t0: Date.now(), tl: performance.now(), introSeen: false });
  const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
  const $ = (sel, ctx = doc) => ctx.querySelector(sel);
  const $$ = (sel, ctx = doc) => Array.from(ctx.querySelectorAll(sel));
  const clamp = (v, lo, hi) => (v < lo ? lo : v > hi ? hi : v);
  const epochNow = () => (performance.timeOrigin ? performance.timeOrigin + performance.now() : Date.now());

  const store = {
    get(key) {
      try { return sessionStorage.getItem(key); } catch (_) { return null; }
    },
    set(key, value) {
      try { sessionStorage.setItem(key, value); } catch (_) { /* storage blocked */ }
    },
  };

  /* ---------- looping header animations: lock their phase to the session clock ---------- */

  // CSS animations start at the first rendered frame, which comes later on heavier pages.
  // Pinning their start to the moment boot.js ran makes the --clock delay exact, so the
  // scan line and recording light continue seamlessly from the previous page.
  const LOOPS = new Set(['rec', 'header-scan', 'marquee']);

  function syncLoops() {
    if (typeof doc.getAnimations !== 'function' || typeof boot.tl !== 'number') return;
    for (const a of doc.getAnimations()) {
      if (LOOPS.has(a.animationName)) a.startTime = boot.tl;
    }
  }

  /* ---------- header height: keeps focused and anchored content clear of the sticky bar ---------- */

  function initTopbarMetrics() {
    const bar = $('.topbar');
    if (!bar) return;
    let last = 0;
    const set = () => {
      const h = Math.ceil(bar.getBoundingClientRect().height);
      if (h && h !== last) {
        last = h;
        root.style.setProperty('--topbar-h', h + 'px');
      }
    };
    set();
    if ('ResizeObserver' in window) new ResizeObserver(set).observe(bar);
    else window.addEventListener('resize', set, { passive: true });
  }

  /* ---------- DTG clock (military date-time group, UTC) ---------- */

  const MONTHS = ['JAN', 'FEB', 'MAR', 'APR', 'MAY', 'JUN', 'JUL', 'AUG', 'SEP', 'OCT', 'NOV', 'DEC'];
  const pad = (n) => String(n).padStart(2, '0');

  function tickDtg() {
    const d = new Date();
    const s = pad(d.getUTCDate()) + pad(d.getUTCHours()) + pad(d.getUTCMinutes()) + 'Z' +
      MONTHS[d.getUTCMonth()] + String(d.getUTCFullYear()).slice(-2);
    $$('.js-dtg').forEach((el) => {
      if (el.textContent !== s) el.textContent = s;
    });
  }

  function initDtg() {
    tickDtg();
    let timer = 0;
    // Wake once a minute, on the minute boundary; the DTG has minute resolution.
    const schedule = () => {
      clearTimeout(timer);
      timer = setTimeout(() => { tickDtg(); schedule(); }, 60000 - (Date.now() % 60000) + 30);
    };
    schedule();
    doc.addEventListener('visibilitychange', () => {
      if (!doc.hidden) { tickDtg(); schedule(); }
    });
    window.addEventListener('pageshow', (e) => {
      if (e.persisted) { tickDtg(); schedule(); }
    });
  }

  /* ---------- smoke background: halftone noise in two WebGL passes ----------
     Pass 1 renders the smoke tone at one texel per halftone cell; the noise is constant
     inside a cell, so the expensive fbm runs ~50-110x less often than per pixel.
     Pass 2 draws the dots at full resolution, sampling the tone and adding the heat spot. */

  const VERT = 'attribute vec2 p;void main(){gl_Position=vec4(p,0.,1.);}';

  const TONE_FRAG = [
    'uniform vec2 uRes;uniform float uTime;uniform float uCell;',
    'float hash(vec2 p){p=fract(p*vec2(123.34,456.21));p+=dot(p,p+45.32);return fract(p.x*p.y);}',
    'float noise(vec2 p){vec2 i=floor(p),f=fract(p);vec2 u=f*f*(3.-2.*f);',
    'return mix(mix(hash(i),hash(i+vec2(1.,0.)),u.x),mix(hash(i+vec2(0.,1.)),hash(i+vec2(1.,1.)),u.x),u.y);}',
    'float fbm(vec2 p){float v=0.,a=.5;for(int i=0;i<4;i++){v+=a*noise(p);p=p*2.03+vec2(1.7,9.2);a*=.5;}return v;}',
    'void main(){',
    '  vec2 uv=(gl_FragCoord.xy-1.)*uCell/uRes.y;', // texel centre -> screen position of a cell
    '  float t=uTime*.035;',
    '  vec2 q=vec2(fbm(uv*1.5+t),fbm(uv*1.5-t+3.1));',
    '  float f=fbm(uv*1.7+1.6*q+vec2(t*.7,-t*.4));',
    '  gl_FragColor=vec4(smoothstep(.3,.8,f),0.,0.,1.);',
    '}',
  ].join('\n');

  const DOT_FRAG = [
    'uniform vec2 uRes;uniform vec2 uHeat;uniform float uCell;uniform vec2 uTexSize;uniform sampler2D uTone;',
    'void main(){',
    '  float c=.8660254,s=.5;',
    '  vec2 g=mat2(c,-s,s,c)*gl_FragCoord.xy/uCell;',
    '  vec2 id=floor(g)+.5;',
    '  vec2 cp=mat2(c,s,-s,c)*(id*uCell);',
    '  float tone=texture2D(uTone,(cp/uCell+1.)/uTexSize).r;',
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

  const HEAT_KEY = 'stk:heat';
  const POINTER_IDLE = 2500; // ms without mouse movement before the heat spot drifts

  function readHeat() {
    // Session storage is same-origin, but treat it as untrusted input anyway.
    const raw = store.get(HEAT_KEY);
    if (!raw) return null;
    try {
      const v = JSON.parse(raw);
      const ok = (n) => typeof n === 'number' && Number.isFinite(n) && n >= -0.5 && n <= 1.5;
      if (!v || !ok(v.x) || !ok(v.y) || typeof v.at !== 'number' || typeof v.p !== 'boolean') return null;
      return v;
    } catch (_) {
      return null;
    }
  }

  function initSmoke() {
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
    if (!gl) return; // the CSS dot pattern on .bg stays as the fallback

    // highp keeps the noise stable as session time grows; mediump GPUs get a shorter period.
    const fmt = gl.getShaderPrecisionFormat && gl.getShaderPrecisionFormat(gl.FRAGMENT_SHADER, gl.HIGH_FLOAT);
    const highp = !!(fmt && fmt.precision > 0);
    const PREC = highp ? 'precision highp float;\n' : 'precision mediump float;\n';
    const PERIOD = highp ? 6 * 3600 : 1200; // seconds; matches boot.js MAX_AGE for highp

    let res = null; // GL objects; rebuilt after a lost context
    let TW = 1;
    let TH = 1;

    const compile = (type, src) => {
      const sh = gl.createShader(type);
      gl.shaderSource(sh, src);
      gl.compileShader(sh);
      return gl.getShaderParameter(sh, gl.COMPILE_STATUS) ? sh : null;
    };
    const program = (fragSrc) => {
      const vs = compile(gl.VERTEX_SHADER, VERT);
      const fs = compile(gl.FRAGMENT_SHADER, PREC + fragSrc);
      if (!vs || !fs) return null;
      const prog = gl.createProgram();
      gl.attachShader(prog, vs);
      gl.attachShader(prog, fs);
      gl.bindAttribLocation(prog, 0, 'p');
      gl.linkProgram(prog);
      return gl.getProgramParameter(prog, gl.LINK_STATUS) ? prog : null;
    };

    const setup = () => {
      const tone = program(TONE_FRAG);
      const dots = program(DOT_FRAG);
      if (!tone || !dots) return false;
      const buf = gl.createBuffer();
      gl.bindBuffer(gl.ARRAY_BUFFER, buf);
      gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 3, -1, -1, 3]), gl.STATIC_DRAW);
      gl.enableVertexAttribArray(0);
      gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 0, 0);
      const tex = gl.createTexture();
      gl.bindTexture(gl.TEXTURE_2D, tex);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
      gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, 1, 1, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
      const fbo = gl.createFramebuffer();
      gl.bindFramebuffer(gl.FRAMEBUFFER, fbo);
      gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, tex, 0);
      const complete = gl.checkFramebufferStatus(gl.FRAMEBUFFER) === gl.FRAMEBUFFER_COMPLETE;
      gl.bindFramebuffer(gl.FRAMEBUFFER, null);
      if (!complete) return false;
      gl.useProgram(dots);
      gl.uniform1i(gl.getUniformLocation(dots, 'uTone'), 0);
      res = {
        tex, fbo, tone, dots,
        t: {
          res: gl.getUniformLocation(tone, 'uRes'),
          time: gl.getUniformLocation(tone, 'uTime'),
          cell: gl.getUniformLocation(tone, 'uCell'),
        },
        d: {
          res: gl.getUniformLocation(dots, 'uRes'),
          heat: gl.getUniformLocation(dots, 'uHeat'),
          cell: gl.getUniformLocation(dots, 'uCell'),
          size: gl.getUniformLocation(dots, 'uTexSize'),
        },
      };
      return true;
    };
    if (!setup()) return;

    let dpr = 1;
    let W = 1;
    let H = 1;
    let cell = 7;
    const resize = () => {
      // The canvas box, not window.innerWidth: classic Windows scrollbars take 15-17 px.
      dpr = Math.min(window.devicePixelRatio || 1, 1.5);
      W = Math.max(1, Math.round((canvas.clientWidth || window.innerWidth) * dpr));
      H = Math.max(1, Math.round((canvas.clientHeight || window.innerHeight) * dpr));
      cell = 7 * dpr;
      canvas.width = W;
      canvas.height = H;
      TW = Math.ceil(W / cell) + 2;
      TH = Math.ceil(H / cell) + 2;
      gl.bindTexture(gl.TEXTURE_2D, res.tex);
      gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, TW, TH, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
    };
    resize();

    // Session time: identical on every page, so the smoke continues across navigations.
    const elapsed = () => Math.max(0, (epochNow() - boot.t0) / 1000);
    const drift = (t) => ({ x: 0.5 + 0.34 * Math.cos(t * 0.13), y: 0.5 + 0.3 * Math.sin(t * 0.19) });

    // Heat spot (fractions of the canvas, y up). Restored from the previous page.
    const saved = readHeat();
    const start0 = drift(elapsed());
    const heat = { x: saved ? saved.x : start0.x, y: saved ? saved.y : start0.y };
    const aim = { x: heat.x, y: heat.y };
    let lastPointer = saved && saved.p
      ? performance.now() - Math.min(POINTER_IDLE, Math.max(0, Date.now() - saved.at))
      : -1e9;

    window.addEventListener('pointermove', (e) => {
      if (e.pointerType !== 'mouse') return;
      aim.x = e.clientX / (canvas.clientWidth || window.innerWidth);
      aim.y = 1 - e.clientY / (canvas.clientHeight || window.innerHeight);
      lastPointer = performance.now();
    }, { passive: true });

    const saveHeat = () => {
      const following = performance.now() - lastPointer < POINTER_IDLE;
      store.set(HEAT_KEY, JSON.stringify({ x: +heat.x.toFixed(4), y: +heat.y.toFixed(4), p: following, at: Date.now() }));
    };
    window.addEventListener('pagehide', saveHeat);
    window.addEventListener('pageswap', saveHeat);

    const draw = () => {
      // Pass 1: tone at cell resolution.
      gl.bindFramebuffer(gl.FRAMEBUFFER, res.fbo);
      gl.viewport(0, 0, TW, TH);
      gl.useProgram(res.tone);
      gl.uniform2f(res.t.res, W, H);
      gl.uniform1f(res.t.time, elapsed() % PERIOD);
      gl.uniform1f(res.t.cell, cell);
      gl.drawArrays(gl.TRIANGLES, 0, 3);
      // Pass 2: dots at full resolution.
      gl.bindFramebuffer(gl.FRAMEBUFFER, null);
      gl.viewport(0, 0, W, H);
      gl.useProgram(res.dots);
      gl.activeTexture(gl.TEXTURE0);
      gl.bindTexture(gl.TEXTURE_2D, res.tex);
      gl.uniform2f(res.d.res, W, H);
      gl.uniform2f(res.d.heat, heat.x * W, heat.y * H);
      gl.uniform1f(res.d.cell, cell);
      gl.uniform2f(res.d.size, TW, TH);
      gl.drawArrays(gl.TRIANGLES, 0, 3);
    };

    let raf = 0;
    let last = 0;
    let lost = false;
    // 30 fps: the smoke is slow. The 4 ms slack keeps an even cadence, because rAF timestamps
    // are vsync-quantised and a strict 33.3 ms test would skip to every third 60 Hz frame.
    const FRAME = 1000 / 30 - 4;
    const loop = (now) => {
      raf = requestAnimationFrame(loop);
      const dt = now - last;
      if (dt < FRAME) return;
      last = now;
      if (now - lastPointer > POINTER_IDLE) {
        const d = drift(elapsed());
        aim.x = d.x;
        aim.y = d.y;
      }
      const a = 1 - Math.exp(-Math.min(dt, 100) / 260);
      heat.x += (aim.x - heat.x) * a;
      heat.y += (aim.y - heat.y) * a;
      draw();
    };

    const start = () => {
      cancelAnimationFrame(raf);
      if (lost) return;
      if (reduceMotion.matches) draw();
      else raf = requestAnimationFrame(loop);
    };

    let rt = 0;
    window.addEventListener('resize', () => {
      clearTimeout(rt);
      rt = setTimeout(() => {
        if (lost) return;
        resize();
        draw();
      }, 120);
    }, { passive: true });
    if (reduceMotion.addEventListener) reduceMotion.addEventListener('change', start);
    canvas.addEventListener('webglcontextlost', (e) => {
      e.preventDefault();
      lost = true;
      cancelAnimationFrame(raf);
    });
    canvas.addEventListener('webglcontextrestored', () => {
      lost = !setup();
      if (!lost) {
        resize();
        draw();
        start();
      }
    });

    // Draw the first frame now: core.js is render-blocking, so the first paint and the
    // incoming view-transition snapshot already show the smoke at the right phase.
    draw();
    start();
  }

  window.STK = Object.freeze({ $, $$, clamp, reduceMotion, store, boot });

  syncLoops();
  initTopbarMetrics();
  initDtg();
  initSmoke();
})();
