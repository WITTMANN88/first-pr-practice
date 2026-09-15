// Posts the FAQ into #faq once (idempotent via footer marker, same
// pattern as rulesPost.js and the other panels).
const { EmbedBuilder } = require('discord.js');

const MARKER = 'stakeout-faq-v1';

const QA = [
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
    a: 'Роль **STAKEOUT FRIEND**: свой цвет ника командой `!color #RRGGBB` и ×1.5 к опыту.',
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
];

async function postFaq(guild) {
  const channel = guild.channels.cache.find((c) => c.name === 'faq');
  if (!channel?.isTextBased()) {
    console.warn('faq channel not found — skipping FAQ post');
    return;
  }

  const recent = await channel.messages.fetch({ limit: 20 });
  const already = recent.find(
    (m) => m.author.id === guild.client.user.id && m.embeds.some((e) => e.footer?.text === MARKER),
  );
  if (already) return;

  const embed = new EmbedBuilder()
    .setTitle('FAQ')
    .addFields(QA.map((item) => ({ name: `❓ ${item.q}`, value: item.a })))
    .setColor(0x8b0000)
    .setFooter({ text: MARKER });

  await channel.send({ embeds: [embed] });
  console.log('Posted FAQ in #faq');
}

module.exports = { postFaq };
