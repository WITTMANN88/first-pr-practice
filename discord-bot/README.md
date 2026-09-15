# STAKEOUT — Discord Bot

Провижининг-скрипт: одним запуском создаёт всю структуру сервера из
[блюпринта](https://claude.ai/artifact/E4Ed6468Ww3oifvrCMoQs7) — роли, категории,
каналы и права доступа. Безопасно перезапускать: всё, что уже существует по имени,
пропускается.

## 1. Требования

- Node.js 18+ ([nodejs.org](https://nodejs.org))
- Уже созданный (пусть даже пустой) Discord-сервер STAKEOUT

## 2. Создать бота

1. Открой [discord.com/developers/applications](https://discord.com/developers/applications) → **New Application** → назови (например `StakeOut Ops`).
2. Вкладка **Bot** → **Reset Token** → скопируй токен, он понадобится ниже. **Никому не показывай и никуда не публикуй.**
3. Там же включи **Privileged Gateway Intents**: `SERVER MEMBERS INTENT` и `MESSAGE CONTENT INTENT` (пригодятся будущим ботам — музыке, XP, тикетам).
4. Вкладка **OAuth2 → URL Generator**:
   - Scopes: `bot`, `applications.commands`
   - Bot Permissions: на первое время проще всего выдать `Administrator` — потом можно сузить.
5. Скопируй сгенерированную ссылку внизу, открой её в браузере, выбери свой сервер STAKEOUT и подтверди приглашение.

## 3. Узнать Server ID (Guild ID)

Discord → Настройки пользователя → Расширенные → включить **Режим разработчика**.
Затем ПКМ по иконке сервера → **Копировать ID сервера**.

## 4. Настроить и запустить

```bash
cd discord-bot
npm install
cp .env.example .env
```

Открой `.env` и впиши:

```
DISCORD_TOKEN=токен_бота_из_шага_2
GUILD_ID=id_сервера_из_шага_3
```

Запуск:

```bash
npm run provision
```

Скрипт выведет в консоль каждую созданную роль/категорию/канал. Полный прогон — секунды-минуты в зависимости от лимитов Discord API.

## Что создаётся

- **Роли персонала**: Admin, Moderator, Helper
- **Звания (лестница XP)**: Recruit → Private → Corporal → Sergeant → Lieutenant → Captain → Major → Commander (создаются пустыми, назначает будущий XP-бот)
- **Access-роли**: Politics, Other Games + отдельная роль на каждую из 12 игр
- **Категории**: START HERE, COMMUNITY, SERIOUS TALK (гейт: Politics), 12 игровых категорий (гейт: своя роль на каждую), OTHER GAMES (гейт: Other Games), RANKS & EVENTS, SUPPORT, STAFF ONLY (гейт: роли персонала)

## Что скрипт **не** делает — донастроить руками

Discord API не даёт полностью автоматизировать это надёжно, проще руками в Server Settings:

- **Rules Screening / Membership Screening** — вставить текст правил из блюпринта, включить экран подтверждения перед входом
- **Verification Level** → High
- Загрузить лого сервера (иконка + баннер)
- Настроить сами кнопки-роли в `#choose-your-roles` (когда будет готов бот ролей)
- Возрастной гейт на `#nsfw-uncensored` (Discord включает это автоматически при `nsfw: true`, но стоит перепроверить в настройках канала)

## Дальше

Следующие кастомные боты (музыка, тикеты, XP, антирейд, YouTube/Twitch-фид,
клип недели) будут отдельными файлами в этой же папке — этот `provision.js`
занимается только структурой сервера и не остаётся работать в фоне.
