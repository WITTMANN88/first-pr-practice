// Locked voice channels at the very top of the list, showing live
// member/boost counts in their own name. Update interval respects
// Discord's channel-rename rate limit (~2 per 10 min per channel).
const { ChannelType, PermissionFlagsBits } = require('discord.js');
const { CATEGORIES } = require('../config');

const CATEGORY_NAME = CATEGORIES.STATS;
const UPDATE_MS = 10 * 60_000;

async function findOrCreateCategory(guild) {
  let category = guild.channels.cache.find((c) => c.type === ChannelType.GuildCategory && c.name === CATEGORY_NAME);
  if (category) return category;

  category = await guild.channels.create({
    name: CATEGORY_NAME,
    type: ChannelType.GuildCategory,
    permissionOverwrites: [{ id: guild.roles.everyone.id, deny: [PermissionFlagsBits.Connect] }],
  });
  console.log(`+ category: ${CATEGORY_NAME}`);

  const categories = [...guild.channels.cache.filter((c) => c.type === ChannelType.GuildCategory).values()].sort(
    (a, b) => a.position - b.position,
  );
  const rest = categories.filter((c) => c.id !== category.id);
  await guild.channels.setPositions([
    { channel: category.id, position: 0 },
    ...rest.map((c, i) => ({ channel: c.id, position: i + 1 })),
  ]);
  console.log(`  positioned ${CATEGORY_NAME} at the top`);

  return category;
}

async function findOrCreateStatChannel(guild, category, keyEmoji, initialName) {
  let channel = guild.channels.cache.find(
    (c) => c.type === ChannelType.GuildVoice && c.parentId === category.id && c.name.startsWith(keyEmoji),
  );
  if (!channel) {
    channel = await guild.channels.create({ name: initialName, type: ChannelType.GuildVoice, parent: category.id });
    console.log(`  + channel: ${initialName}`);
  }
  return channel;
}

async function renameIfChanged(channel, newName) {
  if (channel.name !== newName) await channel.setName(newName).catch(() => {});
}

async function updateStatChannels(guild) {
  const category = await findOrCreateCategory(guild);

  const memberChannel = await findOrCreateStatChannel(guild, category, '👥', `👥 Участников: ${guild.memberCount}`);
  await renameIfChanged(memberChannel, `👥 Участников: ${guild.memberCount}`);

  const boostChannel = await findOrCreateStatChannel(
    guild,
    category,
    '🚀',
    `🚀 Бустов: ${guild.premiumSubscriptionCount ?? 0}`,
  );
  await renameIfChanged(boostChannel, `🚀 Бустов: ${guild.premiumSubscriptionCount ?? 0}`);
}

function startStatTicker(client, guildId) {
  const tick = async () => {
    try {
      const guild = await client.guilds.fetch(guildId);
      await updateStatChannels(guild);
    } catch (err) {
      console.error('Stat channel update failed:', err);
    }
  };
  tick();
  setInterval(tick, UPDATE_MS);
}

module.exports = { startStatTicker };
