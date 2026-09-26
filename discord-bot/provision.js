// STAKEOUT — server provisioning script.
// Creates every role, category and channel from the blueprint in one run.
// Safe to re-run: skips anything that already exists by name.
require('dotenv').config();
const { Client, GatewayIntentBits, ChannelType, PermissionFlagsBits } = require('discord.js');
const {
  STAFF_ROLES,
  RANK_LADDER,
  GAMES,
  CATEGORIES,
  CHANNELS,
  VOICE,
  EXTRA_ROLES,
  LFG_TAGS,
  CONTENT_CREATOR_ROLE,
} = require('./config');

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
  const contentCreatorRole = await findOrCreateRole(guild, CONTENT_CREATOR_ROLE.name, {
    color: CONTENT_CREATOR_ROLE.color,
    hoist: true,
    mentionable: false,
  });

  console.log('== START ==');
  const startHere = await findOrCreateCategory(guild, CATEGORIES.START);
  await findOrCreateChannel(guild, CHANNELS.WELCOME, ChannelType.GuildText, startHere);
  await findOrCreateChannel(guild, CHANNELS.RULES, ChannelType.GuildText, startHere);
  await findOrCreateChannel(guild, CHANNELS.ANNOUNCEMENTS, ChannelType.GuildText, startHere);
  await findOrCreateChannel(guild, CHANNELS.ROLES, ChannelType.GuildText, startHere);
  await findOrCreateChannel(guild, CHANNELS.FAQ, ChannelType.GuildText, startHere);

  console.log('== COMMUNITY ==');
  const community = await findOrCreateCategory(guild, CATEGORIES.COMMUNITY);
  await findOrCreateChannel(guild, CHANNELS.GENERAL, ChannelType.GuildText, community);
  await findOrCreateChannel(guild, CHANNELS.GAMING_TALK, ChannelType.GuildText, community);
  await findOrCreateChannel(guild, CHANNELS.CLIPS, ChannelType.GuildText, community);
  await findOrCreateChannel(guild, CHANNELS.MEMES, ChannelType.GuildText, community);
  await findOrCreateChannel(guild, CHANNELS.SELF_PROMO, ChannelType.GuildText, community);
  await findOrCreateChannel(guild, CHANNELS.CREATORS, ChannelType.GuildText, community, {
    permissionOverwrites: [
      { id: guild.roles.everyone.id, deny: [PermissionFlagsBits.SendMessages] },
      { id: contentCreatorRole.id, allow: [PermissionFlagsBits.SendMessages] },
      ...staffRoleIds.map((id) => ({ id, allow: [PermissionFlagsBits.SendMessages] })),
    ],
  });
  await findOrCreateChannel(guild, CHANNELS.MUSIC, ChannelType.GuildText, community);
  await findOrCreateChannel(guild, CHANNELS.NSFW, ChannelType.GuildText, community, { nsfw: true });
  await findOrCreateChannel(guild, VOICE.LOUNGE, ChannelType.GuildVoice, community);
  await findOrCreateChannel(guild, VOICE.CHILL, ChannelType.GuildVoice, community);

  console.log('== SERIOUS ==');
  const serious = await findOrCreateCategory(
    guild,
    CATEGORIES.SERIOUS,
    gatedOverwrites(guild, [politicsRole.id, ...staffRoleIds]),
  );
  await findOrCreateChannel(guild, CHANNELS.POLITICS, ChannelType.GuildText, serious);

  console.log('== game categories ==');
  for (const g of GAMES) {
    const role = gameRoles[g.key];
    const category = await findOrCreateCategory(
      guild,
      `${g.emoji} ${g.name}`,
      gatedOverwrites(guild, [role.id, ...staffRoleIds]),
    );
    await findOrCreateChannel(guild, CHANNELS.GAME_CHAT, ChannelType.GuildText, category);
    await findOrCreateChannel(guild, CHANNELS.GAME_SQUAD, ChannelType.GuildForum, category, {
      availableTags: LFG_TAGS.map((t) => ({ name: t })),
    });
    await findOrCreateChannel(guild, VOICE.SQUAD_1, ChannelType.GuildVoice, category);
    await findOrCreateChannel(guild, VOICE.SQUAD_2, ChannelType.GuildVoice, category);
    await findOrCreateChannel(guild, VOICE.COMMAND, ChannelType.GuildVoice, category);
    await findOrCreateChannel(guild, VOICE.JOIN_TO_CREATE, ChannelType.GuildVoice, category);
  }

  console.log('== OTHER GAMES ==');
  const otherGames = await findOrCreateCategory(
    guild,
    CATEGORIES.OTHER_GAMES,
    gatedOverwrites(guild, [otherGamesRole.id, ...staffRoleIds]),
  );
  await findOrCreateChannel(guild, CHANNELS.GAME_CHAT, ChannelType.GuildText, otherGames);
  await findOrCreateChannel(guild, CHANNELS.GAME_SQUAD, ChannelType.GuildForum, otherGames, {
    availableTags: LFG_TAGS.map((t) => ({ name: t })),
  });
  await findOrCreateChannel(guild, VOICE.OTHER_GAMES, ChannelType.GuildVoice, otherGames);
  await findOrCreateChannel(guild, VOICE.JOIN_TO_CREATE, ChannelType.GuildVoice, otherGames);

  console.log('== RANKS & EVENTS ==');
  const ranksEvents = await findOrCreateCategory(guild, CATEGORIES.RANKS);
  await findOrCreateChannel(guild, VOICE.BRIEFING, ChannelType.GuildStageVoice, ranksEvents);

  console.log('== SUPPORT ==');
  const support = await findOrCreateCategory(guild, CATEGORIES.SUPPORT);
  await findOrCreateChannel(guild, CHANNELS.TICKET, ChannelType.GuildText, support);

  console.log('== STAFF ==');
  const staffOnly = await findOrCreateCategory(guild, CATEGORIES.STAFF, gatedOverwrites(guild, staffRoleIds));
  await findOrCreateChannel(guild, CHANNELS.MOD_CHAT, ChannelType.GuildText, staffOnly);
  await findOrCreateChannel(guild, CHANNELS.MOD_LOGS, ChannelType.GuildText, staffOnly);
  await findOrCreateChannel(guild, CHANNELS.BAN_LIST, ChannelType.GuildText, staffOnly);
  await findOrCreateChannel(guild, CHANNELS.ALT_FLAGS, ChannelType.GuildText, staffOnly);

  console.log('== AFK ==');
  const afkZone = await findOrCreateCategory(guild, CATEGORIES.AFK);
  const afkChannel = await findOrCreateChannel(guild, VOICE.AFK, ChannelType.GuildVoice, afkZone);
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
