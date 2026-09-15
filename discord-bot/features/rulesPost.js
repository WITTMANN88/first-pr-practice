// Posts the RU+EN rules text into #rules, editing the same message in
// place on later runs (tracked by message ID via messageRegistry, not
// a visible marker).
const { EmbedBuilder } = require('discord.js');
const { upsertPanel } = require('./messageRegistry');

const RU_ARTICLES = [
  'Политика в любом виде (обсуждения, мемы, вбросы, провокации) запрещена везде, кроме категории **SERIOUS TALK**, доступной по роли `Politics` — и там тоже под усиленной модерацией.',
  'Реклама (не обсуждение) чего-либо и ссылки на объекты рекламы запрещены. Исключение — ссылки на свои стримы/видео, только в `#self-promo`.',
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
  'Разрешены только русский и английский языки — все должны понимать, о чём вы пишете.',
];

const EN_ARTICLES = [
  'Politics in any form (discussion, memes, baiting, provocation) is banned everywhere except the **SERIOUS TALK** category, unlocked by the `Politics` role — and even there it\'s under heavy moderation.',
  'Advertising (not discussion) of anything, and links to advertised items, is banned. Exception: links to your own streams/videos, only in `#self-promo`.',
  'Usernames, avatars, statuses and posts may not contain:\n' +
    '  3.1 symbols of terrorist, extremist or otherwise banned organizations or persons;\n' +
    '  3.2 pornographic/erotic content, barring specifically approved exceptions;\n' +
    '  3.3 promotion of violence, drugs, suicide, or cruelty to people or animals;\n' +
    '  3.4 symbols of radical racism or nationalism.',
  'Insulting a person, their family, or their religion is banned.',
  'Off-topic chatter, flooding, and spam are banned.',
  "Streaming content unrelated to a voice channel's topic is discouraged.",
  'Attempting to evade a punishment in any form, including alt accounts, is banned.',
  '`@everyone`/`@here` and mention abuse without good reason are banned.',
  'Voice-channel behavior that disrupts communication is banned.',
  "Only Russian and English are allowed — everyone should understand what you're writing.",
];

function numbered(list) {
  return list.map((text, i) => `**${i + 1}.** ${text}`).join('\n\n');
}

async function postRules(guild) {
  const channel = guild.channels.cache.find((c) => c.name === 'rules');
  if (!channel?.isTextBased()) {
    console.warn('rules channel not found — skipping rules post');
    return;
  }

  const ruEmbed = new EmbedBuilder()
    .setTitle('Правила — RU')
    .setDescription(numbered(RU_ARTICLES))
    .setColor(0x8b0000);

  const enEmbed = new EmbedBuilder().setTitle('Rules — EN').setDescription(numbered(EN_ARTICLES)).setColor(0x8b0000);

  const punishmentEmbed = new EmbedBuilder()
    .setTitle('Наказания / Enforcement')
    .addFields(
      {
        name: 'Стандартная лестница / Standard escalation',
        value: 'Warn → Mute → Kick → Ban\n_Статьи 1 (повторно), 2, 4, 5, 6, 8, 9, 10_',
      },
      {
        name: 'Zero tolerance',
        value: 'Мгновенный бан / Instant ban\n_Статьи 3.1, 3.2, 3.3, 3.4, 7 (все причастные аккаунты)_',
      },
    )
    .setColor(0x8b0000);

  await upsertPanel(channel, 'rules', { embeds: [ruEmbed, enEmbed, punishmentEmbed] });
  console.log('Rules synced in #rules');
}

module.exports = { postRules };
