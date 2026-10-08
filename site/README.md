# STAKEOUT: сайт блога

Статический сайт без сервера, базы данных и админки: HTML, CSS и JS без зависимостей.
Нечему «ломаться» на сервере: нет PHP, CMS, форм входа и SQL.

```
site/
  index.html            разметка, экран загрузки, векторный логотип (inline SVG)
  assets/css/main.css   стили
  assets/js/main.js     WebGL-фон, полутоновые карточки, загрузчик, переходы
  assets/img/favicon.svg
  _headers              заголовки безопасности (Cloudflare Pages / Netlify)
```

## Локальный просмотр (Windows 10)

```powershell
py -m http.server 8080 --directory site
# открыть http://localhost:8080
```

Двойной клик по `index.html` тоже работает, но заголовки из `_headers` применяются
только на хостинге.

## Деплой

Cloudflare Pages: Create project → Connect to Git → этот репозиторий,
Build command пусто, Build output directory `site`. Файл `_headers` подхватывается сам.

## Защита

Уже в коде:

- CSP без `'unsafe-inline'` и `'unsafe-eval'`, Trusted Types включены;
  скрипт пишет текст только через `textContent`, `innerHTML` не используется.
- Запрет встраивания во фрейм (`frame-ancestors 'none'`, `X-Frame-Options`).
- HSTS, `nosniff`, строгий `Referrer-Policy`, отключённые API браузера.
- Ни одной сторонней JS-библиотеки: нет цепочки поставок, которую можно подменить.

Сделать на аккаунтах (основной риск для статического сайта: угон аккаунта, а не взлом кода):

1. 2FA с ключом или приложением на GitHub, хостинге, регистраторе домена и почте.
2. Защита ветки `main` на GitHub: изменения только через pull request.
3. DNSSEC и CAA-запись (`0 issue "letsencrypt.org"` или ваш CA) у регистратора.
4. Cloudflare: Always Use HTTPS, TLS 1.2+, Bot Fight Mode, правило rate limiting.
5. Шрифты перенести в `assets/fonts` и убрать Google Fonts из CSP
   (`style-src 'self'; font-src 'self'`): ноль внешних запросов.
6. Проверить заголовки после деплоя: https://securityheaders.com и https://observatory.mozilla.org.
