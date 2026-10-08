/* STAKEOUT boot. Parser-blocking in <head>, runs before the first paint.
 * Gives every page the same session clock, so looping header animations and the
 * smoke background continue across page loads instead of restarting.
 * No DOM writes besides classes and one CSS custom property (CSSOM, CSP-safe). */
(function () {
  'use strict';

  var root = document.documentElement;
  var MAX_AGE = 6 * 3600 * 1000; // restart the clock after 6 h: keeps shader time small
  var now = Date.now();
  var t0 = now;
  var introSeen = false;

  try {
    var saved = Number(sessionStorage.getItem('stk:t0'));
    if (saved > 0 && saved <= now && now - saved < MAX_AGE) t0 = saved;
    else sessionStorage.setItem('stk:t0', String(t0));
    introSeen = sessionStorage.getItem('stk:intro') === '1';
  } catch (e) {
    // Storage blocked (privacy mode, policy): each page keeps its own clock.
  }

  root.classList.add('js');
  if (introSeen) root.classList.add('intro-seen');
  // Cross-document view transitions (Chromium 126+, Safari 18.2+). Others get a CSS entrance.
  if (!('onpagereveal' in window)) root.classList.add('no-vt');
  // A negative delay starts each infinite animation at the phase it had on the previous page.
  root.style.setProperty('--clock', ((t0 - now) / 1000).toFixed(3) + 's');

  Object.defineProperty(window, 'STK_BOOT', {
    value: Object.freeze({ t0: t0, introSeen: introSeen }),
    writable: false,
    configurable: false,
  });
})();
