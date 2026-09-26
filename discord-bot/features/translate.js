// React to a message with a flag emoji -> bot replies with a
// translation. Uses the unofficial free Google Translate endpoint (no
// API key, no signup) — less stable than a paid API, can rate-limit
// under heavy use, but fine for a community this size.
const { translate } = require('@vitalets/google-translate-api');

const FLAG_TO_LANG = {
  // RU/EN — the server's own languages
  '🇷🇺': 'ru',
  '🇬🇧': 'en',
  '🇺🇸': 'en',
  '🇨🇦': 'en',
  '🇦🇺': 'en',
  // Europe
  '🇩🇪': 'de',
  '🇦🇹': 'de',
  '🇨🇭': 'de',
  '🇫🇷': 'fr',
  '🇪🇸': 'es',
  '🇲🇽': 'es',
  '🇦🇷': 'es',
  '🇵🇹': 'pt',
  '🇧🇷': 'pt',
  '🇮🇹': 'it',
  '🇳🇱': 'nl',
  '🇵🇱': 'pl',
  '🇺🇦': 'uk',
  '🇧🇾': 'be',
  '🇸🇪': 'sv',
  '🇳🇴': 'no',
  '🇩🇰': 'da',
  '🇫🇮': 'fi',
  '🇬🇷': 'el',
  '🇨🇿': 'cs',
  '🇸🇰': 'sk',
  '🇭🇺': 'hu',
  '🇷🇴': 'ro',
  '🇧🇬': 'bg',
  '🇭🇷': 'hr',
  '🇷🇸': 'sr',
  '🇸🇮': 'sl',
  '🇱🇹': 'lt',
  '🇱🇻': 'lv',
  '🇪🇪': 'et',
  // Caucasus / Central Asia
  '🇰🇿': 'kk',
  '🇺🇿': 'uz',
  '🇦🇿': 'az',
  '🇬🇪': 'ka',
  '🇦🇲': 'hy',
  // Middle East / Asia
  '🇹🇷': 'tr',
  '🇸🇦': 'ar',
  '🇮🇱': 'he',
  '🇮🇷': 'fa',
  '🇵🇰': 'ur',
  '🇮🇳': 'hi',
  '🇧🇩': 'bn',
  '🇨🇳': 'zh-cn',
  '🇯🇵': 'ja',
  '🇰🇷': 'ko',
  '🇻🇳': 'vi',
  '🇹🇭': 'th',
  '🇮🇩': 'id',
  '🇵🇭': 'tl',
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

// For third-party text the bot re-posts (Steam blurbs, giveaway
// descriptions) that often comes only in English. Returns the original
// on any failure so a flaky unofficial endpoint never blocks a post.
async function ensureRussian(text) {
  if (!text) return text;
  const latin = (text.match(/[A-Za-z]/g) || []).length;
  const cyrillic = (text.match(/[А-Яа-яЁё]/g) || []).length;
  if (cyrillic >= latin) return text;
  try {
    const result = await translate(text, { to: 'ru' });
    return result.text || text;
  } catch (err) {
    console.warn('  ! auto-translate failed, posting original:', err.message);
    return text;
  }
}

module.exports = { handleReactionAdd, ensureRussian };
