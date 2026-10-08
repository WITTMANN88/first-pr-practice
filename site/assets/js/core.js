/* STAKEOUT core, shared by every page: DTG clock and the smoke background.
 * No dependencies. Text reaches the DOM only through textContent and nothing parses
 * HTML, so the page runs under CSP with Trusted Types enforced ('trusted-types none').
 * Exposes a small frozen helper namespace, window.STK, for page scripts. */
(() => {
  'use strict';

  const doc = document;
  const boot = window.STK_BOOT || Object.freeze({ t0: Date.now(), introSeen: false });
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

  /* ---------- smoke background: halftone noise in a WebGL fragment shader ---------- */

  const VERT = 'attribute vec2 p;void main(){gl_Position=vec4(p,0.,1.);}';
  const FRAG_BODY = [
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
    const PERIOD = highp ? 6 * 3600 : 1200; // seconds; matches boot.js MAX_AGE for highp
    const FRAG = (highp ? 'precision highp float;\n' : 'precision mediump float;\n') + FRAG_BODY;

    let u = null;
    const setup = () => {
      const compile = (type, src) => {
        const sh = gl.createShader(type);
        gl.shaderSource(sh, src);
        gl.compileShader(sh);
        return gl.getShaderParameter(sh, gl.COMPILE_STATUS) ? sh : null;
      };
      const vs = compile(gl.VERTEX_SHADER, VERT);
      const fs = compile(gl.FRAGMENT_SHADER, FRAG);
      if (!vs || !fs) return false;
      const prog = gl.createProgram();
      gl.attachShader(prog, vs);
      gl.attachShader(prog, fs);
      gl.linkProgram(prog);
      if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) return false;
      gl.useProgram(prog);
      const buf = gl.createBuffer();
      gl.bindBuffer(gl.ARRAY_BUFFER, buf);
      gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 3, -1, -1, 3]), gl.STATIC_DRAW);
      const loc = gl.getAttribLocation(prog, 'p');
      gl.enableVertexAttribArray(loc);
      gl.vertexAttribPointer(loc, 2, gl.FLOAT, false, 0, 0);
      u = {
        res: gl.getUniformLocation(prog, 'uRes'),
        time: gl.getUniformLocation(prog, 'uTime'),
        heat: gl.getUniformLocation(prog, 'uHeat'),
        cell: gl.getUniformLocation(prog, 'uCell'),
      };
      return true;
    };
    if (!setup()) return;

    let dpr = 1;
    let W = 1;
    let H = 1;
    const resize = () => {
      dpr = Math.min(window.devicePixelRatio || 1, 1.5);
      W = Math.max(1, Math.round(window.innerWidth * dpr));
      H = Math.max(1, Math.round(window.innerHeight * dpr));
      canvas.width = W;
      canvas.height = H;
      gl.viewport(0, 0, W, H);
    };
    resize();

    // Session time: identical on every page, so the smoke continues across navigations.
    const elapsed = () => Math.max(0, (epochNow() - boot.t0) / 1000);
    const drift = (t) => ({ x: 0.5 + 0.34 * Math.cos(t * 0.13), y: 0.5 + 0.3 * Math.sin(t * 0.19) });

    // Heat spot (fractions of the viewport, y up). Restored from the previous page.
    const saved = readHeat();
    const start0 = drift(elapsed());
    const heat = { x: saved ? saved.x : start0.x, y: saved ? saved.y : start0.y };
    const aim = { x: heat.x, y: heat.y };
    let lastPointer = saved && saved.p ? performance.now() - Math.min(POINTER_IDLE, Math.max(0, Date.now() - saved.at)) : -1e9;

    window.addEventListener('pointermove', (e) => {
      if (e.pointerType !== 'mouse') return;
      aim.x = e.clientX / window.innerWidth;
      aim.y = 1 - e.clientY / window.innerHeight;
      lastPointer = performance.now();
    }, { passive: true });

    const saveHeat = () => {
      const following = performance.now() - lastPointer < POINTER_IDLE;
      store.set(HEAT_KEY, JSON.stringify({ x: +heat.x.toFixed(4), y: +heat.y.toFixed(4), p: following, at: Date.now() }));
    };
    window.addEventListener('pagehide', saveHeat);
    window.addEventListener('pageswap', saveHeat);

    const draw = () => {
      gl.uniform2f(u.res, W, H);
      gl.uniform1f(u.time, elapsed() % PERIOD);
      gl.uniform2f(u.heat, heat.x * W, heat.y * H);
      gl.uniform1f(u.cell, 7 * dpr);
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
      if (!lost) { resize(); start(); }
    });

    // Draw the first frame now: core.js is render-blocking, so the first paint and the
    // incoming view-transition snapshot already show the smoke at the right phase.
    draw();
    start();
  }

  window.STK = Object.freeze({ $, $$, clamp, reduceMotion, store, boot });

  initDtg();
  initSmoke();
})();
