// Posts the rules into #правила, editing the same message in place on
// later runs (tracked by message ID via messageRegistry, not a visible
// marker).
const { EmbedBuilder } = require('discord.js');
const { upsertPanel } = require('./messageRegistry');
const { channelRef } = require('./mentions');
const { CATEGORIES, CHANNELS, EXTRA_ROLES } = require('../config');
const COLORS = require('./colors');

const POLITICS_ROLE = EXTRA_ROLES.find((r) => r.key === 'politics').name;

function articles(guild) {
  return [
    `Политика в любом виде (обсуждения, мемы, вбросы, провокации) запрещена везде, кроме категории **${CATEGORIES.SERIOUS}**, доступной по роли **${POLITICS_ROLE}** — и там тоже под усиленной модерацией.`,
    `Реклама (не обсуждение) чего-либо и ссылки на объекты рекламы запрещены. Исключение — ссылки на свои стримы/видео, только в ${channelRef(guild, CHANNELS.SELF_PROMO)}.`,
    'Никнеймы, аватары, статусы и посты не должны содержать:\n' +
      '  3.1 символику террористических, экстремистских и иных запрещённых организаций и лиц, в т.ч. признанных иноагентами;\n' +
      '  3.2 порнографический/эротический контент, кроме отдельно оговорённых исключений;\n' +
      '  3.3 пропаганду насилия, наркотиков, суицида, жестокости к людям и животным;\n' +
      '  3.4 символику радикального расизма и национализма.',
    'Оскорбления личности, семьи, религии запрещены.',
    'Оффтоп, флуд и спам сообщениями запрещены.',
    'Стрим/трансляция контента не по теме голосового канала — нежелательны.',
    'Попытки обойти наказание в любой форме, включая альт-аккаунты, запрещены.',
    '`@everyone`/`@here` и злоупотребление упоминаниями участников без веской причины запрещены.',
    'Поведение в голосовых каналах, мешающее общению, запрещено.',
  ];
}

function numbered(list) {
  return list.map((text, i) => `**${i + 1}.** ${text}`).join('\n\n');
}

async function postRules(guild) {
  const channel = guild.channels.cache.find((c) => c.name === CHANNELS.RULES);
  if (!channel?.isTextBased()) {
    console.warn('rules channel not found — skipping rules post');
    return;
  }

  const rulesEmbed = new EmbedBuilder()
    .setTitle('Правила сервера')
    .setDescription(numbered(articles(guild)))
    .setColor(COLORS.BRAND);

  const punishmentEmbed = new EmbedBuilder()
    .setTitle('Наказания')
    .addFields(
      {
        name: 'Стандартная лестница',
        value: 'Предупреждение → Мут → Кик → Бан\n_Статьи 1 (повторно), 2, 4, 5, 6, 8, 9_',
      },
      {
        name: 'Без предупреждений',
        value: 'Мгновенный бан\n_Статьи 3.1, 3.2, 3.3, 3.4, 7 (все причастные аккаунты)_',
      },
    )
    .setColor(COLORS.DANGER);

  await upsertPanel(channel, 'rules', { embeds: [rulesEmbed, punishmentEmbed] });
  console.log('Rules synced');
}

module.exports = { postRules };
