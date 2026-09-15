// One-off: colors + emoji icons on every role, moves SUPPORT / STAFF ONLY
// / Tickets above the game categories, and drops the "-lfg" suffix from
// forum channel names. Safe to re-run.
require('dotenv').config();
const { Client, GatewayIntentBits, ChannelType } = require('discord.js');
const { STAFF_ROLES, RANK_LADDER, GAMES, EXTRA_ROLES } = require('../config');

const TOKEN = process.env.DISCORD_TOKEN;
const GUILD_ID = process.env.GUILD_ID;

if (!TOKEN || !GUILD_ID) {
  console.error('Missing DISCORD_TOKEN or GUILD_ID in .env');
  process.exit(1);
}

const client = new Client({ intents: [GatewayIntentBits.Guilds] });

// White -> dark bordeaux progression, matching the field-manual palette.
const RANK_STYLE = [
  { color: '#ffffff', emoji: '🔘' }, // Recruit
  { color: '#e5dbdb', emoji: '⚪' }, // Private
  { color: '#cbb6b6', emoji: '🔸' }, // Corporal
  { color: '#b19292', emoji: '🔶' }, // Sergeant
  { color: '#986d6d', emoji: '🎖️' }, // Lieutenant
  { color: '#7e4949', emoji: '🎖️' }, // Captain
  { color: '#642424', emoji: '🥈' }, // Major
  { color: '#4a0000', emoji: '🥇' }, // Commander
];

const STAFF_EMOJI = { Owner: '⭐', Admin: '👑', Moderator: '🛡️', Helper: '🔧' };

async function styleRole(guild, name, { color, emoji }) {
  const role = guild.roles.cache.find((r) => r.name === name);
  if (!role) {
    console.warn(`  ! role not found: ${name}`);
    return;
  }
  try {
    await role.edit({ color, unicodeEmoji: emoji });
    console.log(`  = styled: ${name}`);
  } catch (err) {
    // Role icons need the guild boosted to level 2 — fall back to color only.
    try {
      await role.edit({ color });
      console.log(`  = styled, no icon (needs server boost lvl 2): ${name}`);
    } catch (err2) {
      console.warn(`  ! failed to style ${name}: ${err2.message}`);
    }
  }
}

async function reorderCategories(guild) {
  const categories = [...guild.channels.cache.filter((c) => c.type === ChannelType.GuildCategory).values()].sort(
    (a, b) => a.position - b.position,
  );

  const moveNames = ['🎫 SUPPORT / ПОДДЕРЖКА', '🔐 STAFF ONLY', '🎫 Tickets'];
  const toMove = categories.filter((c) => moveNames.includes(c.name));
  const rest = categories.filter((c) => !moveNames.includes(c.name));

  const communityIndex = rest.findIndex((c) => c.name === '💬 COMMUNITY');
  const insertAt = communityIndex === -1 ? 0 : communityIndex + 1;

  const newOrder = [...rest.slice(0, insertAt), ...toMove, ...rest.slice(insertAt)];

  await guild.channels.setPositions(newOrder.map((c, i) => ({ channel: c.id, position: i })));
  console.log('Reordered categories:', newOrder.map((c) => c.name).join(' -> '));
}

async function renameLfgChannels(guild) {
  const forums = guild.channels.cache.filter((c) => c.type === ChannelType.GuildForum && c.name.endsWith('-lfg'));
  for (const channel of forums.values()) {
    const oldName = channel.name;
    const newName = oldName.replace(/-lfg$/, '-squad');
    try {
      await channel.setName(newName);
      console.log(`  renamed: ${oldName} -> ${newName}`);
    } catch (err) {
      console.warn(`  ! rename failed for ${oldName}: ${err.message}`);
    }
  }
}

client.once('clientReady', async () => {
  try {
    const guild = await client.guilds.fetch(GUILD_ID);
    await guild.roles.fetch();
    await guild.channels.fetch();

    console.log('== styling staff roles ==');
    for (const r of STAFF_ROLES) {
      await styleRole(guild, r.name, { color: r.color, emoji: STAFF_EMOJI[r.name] });
    }

    console.log('== styling rank ladder ==');
    for (let i = 0; i < RANK_LADDER.length; i++) {
      await styleRole(guild, RANK_LADDER[i], RANK_STYLE[i]);
    }

    console.log('== styling access roles ==');
    for (const g of GAMES) {
      await styleRole(guild, g.name, { color: '#6b6862', emoji: g.emoji });
    }
    for (const r of EXTRA_ROLES) {
      await styleRole(guild, r.name, { emoji: r.emoji });
    }

    console.log('== reordering categories ==');
    await reorderCategories(guild);

    console.log('== renaming LFG forums ==');
    await renameLfgChannels(guild);

    console.log('\nDone.');
    process.exit(0);
  } catch (err) {
    console.error(err);
    process.exit(1);
  }
});

client.login(TOKEN);
