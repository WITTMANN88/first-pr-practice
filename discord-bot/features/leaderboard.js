// Auto-updating XP leaderboard — lives in its own read-only channel
// instead of a chat command, so it doesn't clutter conversation.
const { EmbedBuilder, ChannelType, PermissionFlagsBits } = require('discord.js');
const { getTopUsers } = require('./xp');

const MARKER = 'stakeout-leaderboard-v1';
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

async function updateLeaderboard(guild) {
  const channel = await findOrCreateChannel(guild);
  if (!channel) return;

  const top = getTopUsers(10);
  const lines = top.length
    ? await Promise.all(
        top.map(async (entry, i) => {
          const member = await guild.members.fetch(entry.userId).catch(() => null);
          const name = member ? member.user.tag : `бывший участник (${entry.userId})`;
          return `**${i + 1}.** ${name} — ${entry.xp} XP (${entry.rank})`;
        }),
      )
    : ['_пока никто не набрал XP_'];

  const embed = new EmbedBuilder()
    .setTitle('🏆 Топ по XP')
    .setDescription(lines.join('\n'))
    .setColor(0x8b0000)
    .setTimestamp()
    .setFooter({ text: MARKER });

  const recent = await channel.messages.fetch({ limit: 10 });
  const existing = recent.find(
    (m) => m.author.id === guild.client.user.id && m.embeds.some((e) => e.footer?.text === MARKER),
  );
  if (existing) {
    await existing.edit({ embeds: [embed] });
  } else {
    await channel.send({ embeds: [embed] });
  }
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
