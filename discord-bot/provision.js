// STAKEOUT — server provisioning script.
// Creates every role, category and channel from the blueprint in one run.
// Safe to re-run: skips anything that already exists by name.
require('dotenv').config();
const { Client, GatewayIntentBits, ChannelType, PermissionFlagsBits } = require('discord.js');
const { STAFF_ROLES, RANK_LADDER, GAMES, EXTRA_ROLES, LFG_TAGS } = require('./config');

const TOKEN = process.env.DISCORD_TOKEN;
const GUILD_ID = process.env.GUILD_ID;

if (!TOKEN || !GUILD_ID) {
  console.error('Missing DISCORD_TOKEN or GUILD_ID — copy .env.example to .env and fill both in.');
  process.exit(1);
}

const politicsRoleDef = EXTRA_ROLES.find((r) => r.key === 'politics');
const otherGamesRoleDef = EXTRA_ROLES.find((r) => r.key === 'other-games');

const client = new Client({ intents: [GatewayIntentBits.Guilds] });

async function findOrCreateRole(guild, name, opts = {}) {
  const existing = guild.roles.cache.find((r) => r.name === name);
  if (existing) return existing;
  const role = await guild.roles.create({ name, ...opts });
  console.log(`  + role: ${name}`);
  return role;
}

async function findOrCreateCategory(guild, name, permissionOverwrites) {
  const existing = guild.channels.cache.find((c) => c.type === ChannelType.GuildCategory && c.name === name);
  if (existing) return existing;
  const category = await guild.channels.create({ name, type: ChannelType.GuildCategory, permissionOverwrites });
  console.log(`+ category: ${name}`);
  return category;
}

async function findOrCreateChannel(guild, name, type, parent, extra = {}) {
  const existing = guild.channels.cache.find((c) => c.type === type && c.name === name && c.parentId === parent.id);
  if (existing) return existing;
  try {
    const channel = await guild.channels.create({ name, type, parent: parent.id, ...extra });
    console.log(`  + channel: ${name}`);
    return channel;
  } catch (err) {
    console.warn(`  ! skipped channel "${name}": ${err.message}`);
    return null;
  }
}

function gatedOverwrites(guild, allowedRoleIds) {
  return [
    { id: guild.roles.everyone.id, deny: [PermissionFlagsBits.ViewChannel] },
    ...allowedRoleIds.map((id) => ({ id, allow: [PermissionFlagsBits.ViewChannel] })),
  ];
}

async function main() {
  const guild = await client.guilds.fetch(GUILD_ID);
  await guild.roles.fetch();
  await guild.channels.fetch();

  console.log('== staff roles ==');
  const staffRoles = {};
  for (const r of STAFF_ROLES) {
    staffRoles[r.name] = await findOrCreateRole(guild, r.name, {
      color: r.color,
      hoist: r.hoist,
      permissions: r.permissions,
    });
  }
  const staffRoleIds = Object.values(staffRoles).map((r) => r.id);

  console.log('== rank ladder ==');
  for (const name of RANK_LADDER) {
    await findOrCreateRole(guild, name, { hoist: false, mentionable: false });
  }

  console.log('== access roles ==');
  const politicsRole = await findOrCreateRole(guild, politicsRoleDef.name, { color: 'DarkRed', mentionable: false });
  const otherGamesRole = await findOrCreateRole(guild, otherGamesRoleDef.name, { color: 'Blue', mentionable: false });
  const gameRoles = {};
  for (const g of GAMES) {
    gameRoles[g.key] = await findOrCreateRole(guild, g.name, { mentionable: false });
  }

  console.log('== START HERE ==');
  const startHere = await findOrCreateCategory(guild, '📋 START HERE');
  await findOrCreateChannel(guild, 'welcome', ChannelType.GuildText, startHere);
  await findOrCreateChannel(guild, 'rules', ChannelType.GuildText, startHere);
  await findOrCreateChannel(guild, 'announcements', ChannelType.GuildText, startHere);
  await findOrCreateChannel(guild, 'choose-your-roles', ChannelType.GuildText, startHere);
  await findOrCreateChannel(guild, 'faq', ChannelType.GuildText, startHere);

  console.log('== COMMUNITY ==');
  const community = await findOrCreateCategory(guild, '💬 COMMUNITY');
  await findOrCreateChannel(guild, 'general', ChannelType.GuildText, community);
  await findOrCreateChannel(guild, 'gaming-talk', ChannelType.GuildText, community);
  await findOrCreateChannel(guild, 'clips-screenshots', ChannelType.GuildText, community);
  await findOrCreateChannel(guild, 'memes', ChannelType.GuildText, community);
  await findOrCreateChannel(guild, 'self-promo', ChannelType.GuildText, community);
  await findOrCreateChannel(guild, 'music-commands', ChannelType.GuildText, community);
  await findOrCreateChannel(guild, 'nsfw-uncensored', ChannelType.GuildText, community, { nsfw: true });
  await findOrCreateChannel(guild, '🔊 Lounge', ChannelType.GuildVoice, community);
  await findOrCreateChannel(guild, '🔊 Chill', ChannelType.GuildVoice, community);

  console.log('== SERIOUS TALK ==');
  const serious = await findOrCreateCategory(
    guild,
    '🌍 SERIOUS TALK',
    gatedOverwrites(guild, [politicsRole.id, ...staffRoleIds]),
  );
  await findOrCreateChannel(guild, 'politics-and-irl', ChannelType.GuildText, serious);

  console.log('== game categories ==');
  for (const g of GAMES) {
    const role = gameRoles[g.key];
    const category = await findOrCreateCategory(
      guild,
      `${g.emoji} ${g.name}`,
      gatedOverwrites(guild, [role.id, ...staffRoleIds]),
    );
    await findOrCreateChannel(guild, `${g.key}-chat`, ChannelType.GuildText, category);
    await findOrCreateChannel(guild, `${g.key}-squad`, ChannelType.GuildForum, category, {
      availableTags: LFG_TAGS.map((t) => ({ name: t })),
    });
    await findOrCreateChannel(guild, 'Squad 1', ChannelType.GuildVoice, category);
    await findOrCreateChannel(guild, 'Squad 2', ChannelType.GuildVoice, category);
    await findOrCreateChannel(guild, 'Command', ChannelType.GuildVoice, category);
    await findOrCreateChannel(guild, '➕ Join to Create', ChannelType.GuildVoice, category);
  }

  console.log('== OTHER GAMES ==');
  const otherGames = await findOrCreateCategory(
    guild,
    '🎲 OTHER GAMES',
    gatedOverwrites(guild, [otherGamesRole.id, ...staffRoleIds]),
  );
  await findOrCreateChannel(guild, 'other-games-chat', ChannelType.GuildText, otherGames);
  await findOrCreateChannel(guild, 'other-games-squad', ChannelType.GuildForum, otherGames, {
    availableTags: LFG_TAGS.map((t) => ({ name: t })),
  });
  await findOrCreateChannel(guild, '🔊 Other Games', ChannelType.GuildVoice, otherGames);
  await findOrCreateChannel(guild, '➕ Join to Create', ChannelType.GuildVoice, otherGames);

  console.log('== RANKS & EVENTS ==');
  const ranksEvents = await findOrCreateCategory(guild, '🎖️ RANKS & EVENTS');
  await findOrCreateChannel(guild, 'Command Briefing', ChannelType.GuildStageVoice, ranksEvents);

  console.log('== SUPPORT ==');
  const support = await findOrCreateCategory(guild, '🎫 SUPPORT');
  await findOrCreateChannel(guild, 'open-a-ticket', ChannelType.GuildText, support);

  console.log('== STAFF ONLY ==');
  const staffOnly = await findOrCreateCategory(guild, '🔐 STAFF ONLY', gatedOverwrites(guild, staffRoleIds));
  await findOrCreateChannel(guild, 'mod-chat', ChannelType.GuildText, staffOnly);
  await findOrCreateChannel(guild, 'mod-logs', ChannelType.GuildText, staffOnly);
  await findOrCreateChannel(guild, 'ban-list', ChannelType.GuildText, staffOnly);
  await findOrCreateChannel(guild, 'alt-flags', ChannelType.GuildText, staffOnly);

  console.log('== AFK ZONE ==');
  const afkZone = await findOrCreateCategory(guild, '💤 AFK ZONE');
  const afkChannel = await findOrCreateChannel(guild, '💤 AFK', ChannelType.GuildVoice, afkZone);
  await guild.setAFKChannel(afkChannel);
  await guild.setAFKTimeout(300);

  console.log('\nDone — server structure provisioned.');
  process.exit(0);
}

client.once('ready', () => {
  console.log(`Logged in as ${client.user.tag}`);
  main().catch((err) => {
    console.error(err);
    process.exit(1);
  });
});

client.login(TOKEN);
