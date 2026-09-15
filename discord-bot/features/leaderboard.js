// Auto-updating boards — XP and coins — in one read-only channel
// instead of chat commands, so they don't clutter conversation.
const { EmbedBuilder, ChannelType, PermissionFlagsBits } = require('discord.js');
const { getTopUsers } = require('./xp');
const { getTopBalances } = require('./economy');
const { upsertPanel } = require('./messageRegistry');
const COLORS = require('./colors');

const CATEGORY_NAME = '🎖️ RANKS & EVENTS';
const CHANNEL_NAME = 'leaderboard';
const UPDATE_MS = 5 * 60_000;

async function findOrCreateChannel(guild) {
  const category = guild.channels.cache.find((c) => c.type === ChannelType.GuildCategory && c.name === CATEGORY_NAME);
  if (!category) {
    console.warn('RANKS & EVENTS category not found — skipping leaderboard');
    return null;
  }
  let channel = guild.channels.cache.find((c) => c.name === CHANNEL_NAME && c.parentId === category.id);
  if (!channel) {
    channel = await guild.channels.create({
      name: CHANNEL_NAME,
      type: ChannelType.GuildText,
      parent: category.id,
      permissionOverwrites: [{ id: guild.roles.everyone.id, deny: [PermissionFlagsBits.SendMessages] }],
    });
    console.log('+ channel: leaderboard');
  }
  return channel;
}

async function nameFor(guild, userId) {
  const member = await guild.members.fetch(userId).catch(() => null);
  return member ? member.user.tag : `бывший участник (${userId})`;
}

async function updateXpBoard(channel, guild) {
  const top = getTopUsers(10);
  const lines = top.length
    ? await Promise.all(
        top.map(async (entry, i) => `**${i + 1}.** ${await nameFor(guild, entry.userId)} — ${entry.xp} XP (${entry.rank})`),
      )
    : ['_пока никто не набрал XP_'];

  const embed = new EmbedBuilder()
    .setTitle('🏆 Топ по XP')
    .setDescription(lines.join('\n'))
    .setColor(COLORS.BRAND)
    .setTimestamp();

  await upsertPanel(channel, 'leaderboard-xp', { embeds: [embed] });
}

async function updateCoinsBoard(channel, guild) {
  const top = getTopBalances(10);
  const lines = top.length
    ? await Promise.all(top.map(async (entry, i) => `**${i + 1}.** ${await nameFor(guild, entry.userId)} — 🪙 ${entry.balance}`))
    : ['_пока ни у кого нет монет — `!daily` в общем чате_'];

  const embed = new EmbedBuilder()
    .setTitle('🪙 Топ по монетам')
    .setDescription(lines.join('\n'))
    .setColor(COLORS.BRAND)
    .setTimestamp();

  await upsertPanel(channel, 'leaderboard-coins', { embeds: [embed] });
}

async function updateLeaderboard(guild) {
  const channel = await findOrCreateChannel(guild);
  if (!channel) return;
  await updateXpBoard(channel, guild);
  await updateCoinsBoard(channel, guild);
}

function startLeaderboardTicker(client, guildId) {
  const tick = async () => {
    try {
      const guild = await client.guilds.fetch(guildId);
      await updateLeaderboard(guild);
    } catch (err) {
      console.error('Leaderboard update failed:', err);
    }
  };
  tick();
  setInterval(tick, UPDATE_MS);
}

module.exports = { startLeaderboardTicker };
