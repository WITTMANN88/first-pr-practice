// Posts the FAQ into #вопросы-и-ответы — edits the existing post in
// place (tracked by message ID via messageRegistry) so updating QA here
// propagates on next restart.
const { EmbedBuilder } = require('discord.js');
const { upsertPanel } = require('./messageRegistry');
const { channelRef } = require('./mentions');
const { CATEGORIES, CHANNELS, VOICE, EXTRA_ROLES, BOOSTER_ROLE_NAME } = require('../config');
const COLORS = require('./colors');

const POLITICS_ROLE = EXTRA_ROLES.find((r) => r.key === 'politics').name;
const OTHER_GAMES_ROLE = EXTRA_ROLES.find((r) => r.key === 'other-games').name;

// Embed field names don't render channel mentions, so questions use
// plain #names and only answers use clickable refs.
function questions(guild) {
  const ref = (name) => channelRef(guild, name);
  return [
    {
      q: 'Как открыть доступ к играм?',
      a: `Выбери роли в ${ref(CHANNELS.ROLES)} (или сразу на входе, в вопросах при подтверждении правил) — каждая роль открывает свою категорию: чат, поиск отряда и голосовые.`,
    },
    {
      q: 'Как работают звания и опыт?',
      a: `Опыт копится с сообщений (раз в минуту, чтобы не спамили) и с времени в голосовых (кроме АФК). Дошёл до порога — звание меняется само, объявление и топ-10 — в ${ref(CHANNELS.LEADERBOARD)}.`,
    },
    {
      q: 'Как создать обращение?',
      a: `В ${ref(CHANNELS.TICKET)} — кнопка своей категории (жалоба/апелляция/предложение/баг), дальше форма с описанием. Создастся приватный канал, видимый только тебе и персоналу.`,
    },
    {
      q: 'Меня забанили/замьютили по ошибке — что делать?',
      a: `Создай обращение с категорией «Апелляция» в ${ref(CHANNELS.TICKET)} и опиши ситуацию — персонал разберётся.`,
    },
    {
      q: 'Можно обсуждать политику?',
      a: `Только в категории **${CATEGORIES.SERIOUS}**, которая открывается ролью **${POLITICS_ROLE}** в ${ref(CHANNELS.ROLES)}. Везде больше — нельзя, даже мемы.`,
    },
    {
      q: 'Как заказать музыку?',
      a: `В ${ref(CHANNELS.MUSIC)}: \`!play <название или ссылка>\` — играет YouTube/SoundCloud напрямую, ссылки Spotify тоже принимает (ищет трек по названию). Полный список команд закреплён там же.`,
    },
    {
      q: 'Что даёт буст сервера?',
      a: `Роль **${BOOSTER_ROLE_NAME}**: свой цвет ника командой \`!color #RRGGBB\` и ×1.5 к опыту и монетам за \`!daily\`.`,
    },
    {
      q: 'Как пожаловаться на нарушителя?',
      a: `Обращение с категорией «Жалоба» в ${ref(CHANNELS.TICKET)} — это надёжнее, чем писать в общий чат.`,
    },
    {
      q: 'Есть голосовой под свой отряд, а не общий?',
      a: `Да — зайди в «${VOICE.JOIN_TO_CREATE}» в нужной игровой категории, бот сам создаст личный канал и перекинет тебя туда. Опустеет — удалится сам.`,
    },
    {
      q: 'Моей игры нет в списке ролей — что делать?',
      a: `Бери роль **${OTHER_GAMES_ROLE}** — она открывает общую категорию для всего, что не попало в основной список.`,
    },
    {
      q: 'Как быстро пожаловаться на конкретное сообщение?',
      a: 'ПКМ (или зажать на телефоне) по сообщению → «Приложения» → «Пожаловаться» → короткая форма с причиной. Создастся приватный канал с деталями — быстрее, чем создавать обращение вручную.',
    },
    {
      q: 'Как перевести сообщение на свой язык?',
      a: 'Поставь на него реакцию-флаг нужного языка (🇷🇺, 🇬🇧, 🇩🇪, 🇫🇷, 🇵🇱, 🇺🇦 и десятки других) — бот ответит переводом. Перевод работает через бесплатный неофициальный сервис, иногда может ошибаться или тормозить.',
    },
    {
      q: `Откуда берутся раздачи бесплатных игр в #${CHANNELS.ANNOUNCEMENTS}?`,
      a: 'Бот сам проверяет актуальные бесплатные раздачи (Steam/Epic/GOG и т.д.) каждые 30 минут и публикует новые — ничего делать не нужно, просто следи за каналом.',
    },
  ];
}

async function postFaq(guild) {
  const channel = guild.channels.cache.find((c) => c.name === CHANNELS.FAQ);
  if (!channel?.isTextBased()) {
    console.warn('faq channel not found — skipping FAQ post');
    return;
  }

  const embed = new EmbedBuilder()
    .setTitle('Вопросы и ответы')
    .addFields(questions(guild).map((item) => ({ name: `❓ ${item.q}`, value: item.a })))
    .setColor(COLORS.BRAND);

  await upsertPanel(channel, 'faq', { embeds: [embed] });
  console.log('FAQ synced');
}

module.exports = { postFaq };
