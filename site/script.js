/*
 * STAKEOUT — прелоадер.
 *
 * Время показа (2.5 с) задаёт style.css: переменная --preloader-duration.
 * Слой скрывается CSS-анимацией, поэтому сайт откроется и без JavaScript.
 * Скрипт после этого удаляет слой из DOM.
 *
 * Обычный скрипт с defer, не type="module": модули не грузятся
 * при открытии index.html двойным кликом (file://).
 */
(() => {
    'use strict';

    const preloader = document.getElementById('preloader');
    if (!preloader) {
        return;
    }

    const remove = () => preloader.remove();

    // Скрипт выполнился уже после скрытия слоя (медленная сеть) — удаляем сразу.
    if (getComputedStyle(preloader).visibility === 'hidden') {
        remove();
        return;
    }

    preloader.addEventListener('animationend', remove, { once: true });
})();
