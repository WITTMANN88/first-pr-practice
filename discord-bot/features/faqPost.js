// Posts the FAQ into #faq — edits the existing post in place (tracked
// by message ID via messageRegistry) so updating QA here propagates
// on next restart. Bilingual: RU and EN embeds in the same message.
const { EmbedBuilder } = require('discord.js');
const { upsertPanel } = require('./messageRegistry');
const COLORS = require('./colors');

const QA_RU = [
  {
    q: 'Как открыть доступ к играм?',
    a: 'Выбери роли в `#choose-your-roles` (или сразу на входе, в вопросах при подтверждении правил) — каждая роль открывает свою категорию: чат, поиск отряда и голосовые.',
  },
  {
    q: 'Как работают звания и XP?',
    a: 'Опыт копится с сообщений (раз в минуту, чтобы не спамили) и с времени в войсе (кроме AFK-канала). Дошёл до порога — роль звания меняется сама, объявление в `#general`. Топ-10 смотри в `#leaderboard`.',
  },
  {
    q: 'Как открыть тикет?',
    a: 'В `#open-a-ticket` — кнопка своей категории (жалоба/апелляция/предложение/баг), дальше форма с описанием. Создастся приватный канал, видимый только тебе и персоналу.',
  },
  {
    q: 'Меня забанили/замьютили по ошибке — что делать?',
    a: 'Открой тикет с категорией «Апелляция» в `#open-a-ticket` и опиши ситуацию — персонал разберётся.',
  },
  {
    q: 'Можно обсуждать политику?',
    a: 'Только в категории **SERIOUS TALK**, которая открывается ролью `Politics` в `#choose-your-roles`. Везде больше — нельзя, даже мемы.',
  },
  {
    q: 'Как заказать музыку?',
    a: 'В `#music-commands`: `!play <название или ссылка>` — играет YouTube/SoundCloud напрямую, Spotify-ссылки тоже принимает (ищет трек по названию). Полный список команд там же закреплён.',
  },
  {
    q: 'Что даёт буст сервера?',
    a: 'Роль **STAKEOUT FRIEND**: свой цвет ника командой `!color #RRGGBB` и ×1.5 к опыту и монетам за `!daily`.',
  },
  {
    q: 'Как пожаловаться на нарушителя?',
    a: 'Тикет с категорией «Жалоба» в `#open-a-ticket` — это надёжнее, чем писать в общий чат.',
  },
  {
    q: 'Есть голосовой под конкретный отряд, а не общий?',
    a: 'Да — зайди в «➕ Join to Create» в нужной игровой категории, бот сам создаст личный канал и перекинет тебя туда. Опустеет — удалится сам.',
  },
  {
    q: 'Моей игры нет в списке ролей — что делать?',
    a: 'Бери роль `Other Games` — она открывает общую категорию для всего, что не попало в основной список.',
  },
  {
    q: 'Как быстро пожаловаться на конкретное сообщение?',
    a: 'ПКМ (или зажать на телефоне) по сообщению → «Пожаловаться» → короткая форма с причиной. Создастся приватный канал с деталями — быстрее, чем открывать тикет вручную.',
  },
  {
    q: 'Как перевести сообщение на свой язык?',
    a: 'Поставь на него реакцию-флаг нужного языка (🇷🇺, 🇬🇧, 🇩🇪, 🇫🇷, 🇵🇱, 🇺🇦 и десятки других) — бот ответит переводом. Перевод работает через бесплатный неофициальный сервис, иногда может ошибаться или тормозить.',
  },
  {
    q: 'Откуда берутся раздачи бесплатных игр в announcements?',
    a: 'Бот сам проверяет актуальные бесплатные раздачи (Steam/Epic/GOG и т.д.) каждые 30 минут и постит новые — ничего делать не нужно, просто следи за каналом.',
  },
];

const QA_EN = [
  {
    q: 'How do I unlock access to games?',
    a: 'Pick roles in `#choose-your-roles` (or right at the door, in the rules-confirmation questions) — each role unlocks its own category: chat, squad-finder and voice channels.',
  },
  {
    q: 'How do ranks and XP work?',
    a: "XP builds up from messages (once a minute, so spamming doesn't help) and from voice time (except the AFK channel). Hit a threshold and your rank role updates on its own, announced in `#general`. Check the top 10 in `#leaderboard`.",
  },
  {
    q: 'How do I open a ticket?',
    a: "In `#open-a-ticket` — pick your category's button (complaint/appeal/suggestion/bug), then fill in the form. A private channel is created, visible only to you and staff.",
  },
  {
    q: 'I got banned/muted by mistake — what do I do?',
    a: 'Open a ticket with the "Apeal" category in `#open-a-ticket` and describe the situation — staff will sort it out.',
  },
  {
    q: 'Can I discuss politics?',
    a: 'Only in the **SERIOUS TALK** category, unlocked by the `Politics` role in `#choose-your-roles`. Nowhere else — not even memes.',
  },
  {
    q: 'How do I queue music?',
    a: 'In `#music-commands`: `!play <name or link>` — plays YouTube/SoundCloud directly, Spotify links work too (it searches the track by name). Full command list is pinned there.',
  },
  {
    q: 'What does boosting the server give me?',
    a: 'The **STAKEOUT FRIEND** role: your own nickname color via `!color #RRGGBB` and ×1.5 XP and `!daily` coins.',
  },
  {
    q: 'How do I report a rule-breaker?',
    a: 'A "Complaint" ticket in `#open-a-ticket` — more reliable than posting in general chat.',
  },
  {
    q: "Is there a voice channel for my own squad, not a shared one?",
    a: 'Yes — join "➕ Join to Create" in the relevant game category, the bot creates a personal channel and moves you there. It deletes itself once empty.',
  },
  {
    q: "My game isn't in the role list — what do I do?",
    a: 'Take the `Other Games` role — it unlocks the catch-all category for anything not on the main list.',
  },
  {
    q: 'How do I quickly report a specific message?',
    a: 'Right-click (or long-press on mobile) the message → "Report" → a short form with the reason. Creates a private channel with the details — faster than opening a ticket by hand.',
  },
  {
    q: 'How do I translate a message into my language?',
    a: "React to it with the flag of the language you want (🇷🇺, 🇬🇧, 🇩🇪, 🇫🇷, 🇵🇱, 🇺🇦 and dozens more) — the bot replies with a translation. It runs on a free unofficial service, so it can occasionally be wrong or slow.",
  },
  {
    q: 'Where do the free-game giveaways in announcements come from?',
    a: 'The bot checks for active free giveaways (Steam/Epic/GOG etc.) every 30 minutes and posts new ones — nothing to do, just watch the channel.',
  },
];

async function postFaq(guild) {
  const channel = guild.channels.cache.find((c) => c.name === 'faq');
  if (!channel?.isTextBased()) {
    console.warn('faq channel not found — skipping FAQ post');
    return;
  }

  const ruEmbed = new EmbedBuilder()
    .setTitle('FAQ — RU')
    .addFields(QA_RU.map((item) => ({ name: `❓ ${item.q}`, value: item.a })))
    .setColor(COLORS.BRAND);

  const enEmbed = new EmbedBuilder()
    .setTitle('FAQ — EN')
    .addFields(QA_EN.map((item) => ({ name: `❓ ${item.q}`, value: item.a })))
    .setColor(COLORS.BRAND);

  await upsertPanel(channel, 'faq', { embeds: [ruEmbed, enEmbed] });
  console.log('FAQ synced in #faq');
}

module.exports = { postFaq };
