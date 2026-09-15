// React to a message with a flag emoji -> bot replies with a
// translation. Uses the unofficial free Google Translate endpoint (no
// API key, no signup) — less stable than a paid API, can rate-limit
// under heavy use, but fine for a community this size.
const { translate } = require('@vitalets/google-translate-api');

const FLAG_TO_LANG = {
  '🇷🇺': 'ru',
  '🇬🇧': 'en',
  '🇺🇸': 'en',
  '🇩🇪': 'de',
  '🇫🇷': 'fr',
  '🇪🇸': 'es',
  '🇵🇱': 'pl',
  '🇺🇦': 'uk',
  '🇹🇷': 'tr',
  '🇨🇳': 'zh-cn',
  '🇯🇵': 'ja',
  '🇰🇷': 'ko',
  '🇮🇹': 'it',
  '🇵🇹': 'pt',
  '🇧🇷': 'pt',
};

async function handleReactionAdd(reaction, user) {
  if (user.bot) return;
  const lang = FLAG_TO_LANG[reaction.emoji.name];
  if (!lang) return;

  try {
    if (reaction.partial) await reaction.fetch();
    const message = reaction.message.partial ? await reaction.message.fetch() : reaction.message;
    if (!message.content?.trim()) return;

    const result = await translate(message.content, { to: lang });
    if (result.text.trim().toLowerCase() === message.content.trim().toLowerCase()) return; // already in that language

    await message.reply({ content: `🌐 ${result.text}`, allowedMentions: { repliedUser: false } });
  } catch (err) {
    console.error('Translation failed:', err.message);
  }
}

module.exports = { handleReactionAdd };
