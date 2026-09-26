// One-off migration: renames the live server's categories, channels,
// voice rooms, forum tags, roles and AutoMod rules from English to the
// Russian names in config.js — in place, so IDs (and everything keyed
// on them: members' roles, the panel-message registry, Welcome Screen,
// Onboarding) stay intact. Also rewrites stored rank names in
// data/xp.json so nobody gets a bogus "new rank" announcement.
//
// REST-only (no gateway login), since gateway handshakes have been
// flaky from this environment. Run with --dry-run first to see the plan.
// Safe to re-run: anything already renamed is skipped.
require('dotenv').config();
const fs = require('fs');
const path = require('path');
const { REST, Routes, ChannelType } = require('discord.js');
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
  BOOSTER_ROLE_NAME,
} = require('../config');

const TOKEN = process.env.DISCORD_TOKEN;
const GUILD_ID = process.env.GUILD_ID;
const DRY_RUN = process.argv.includes('--dry-run');
const REASON = 'Перевод сервера на русский';
const XP_PATH = path.join(__dirname, '..', 'data', 'xp.json');

if (!TOKEN || !GUILD_ID) {
  console.error('Missing DISCORD_TOKEN or GUILD_ID in .env');
  process.exit(1);
}

const byKey = (list, key) => list.find((r) => r.key === key).name;

const CATEGORY_RENAMES = {
  '📋 START HERE': CATEGORIES.START,
  '💬 COMMUNITY': CATEGORIES.COMMUNITY,
  '🎫 SUPPORT / ПОДДЕРЖКА': CATEGORIES.SUPPORT,
  '🔐 STAFF ONLY': CATEGORIES.STAFF,
  '🎫 Tickets': CATEGORIES.TICKETS,
  '🌍 SERIOUS TALK / СЕРЬЁЗНЫЙ РАЗГОВОР': CATEGORIES.SERIOUS,
  '🎲 OTHER GAMES': CATEGORIES.OTHER_GAMES,
  '🎖️ RANKS & EVENTS': CATEGORIES.RANKS,
  '💤 AFK ZONE': CATEGORIES.AFK,
};

// Unique names, renamed wherever they are.
const CHANNEL_RENAMES = {
  welcome: CHANNELS.WELCOME,
  rules: CHANNELS.RULES,
  announcements: CHANNELS.ANNOUNCEMENTS,
  'choose-your-roles': CHANNELS.ROLES,
  faq: CHANNELS.FAQ,
  general: CHANNELS.GENERAL,
  'gaming-talk': CHANNELS.GAMING_TALK,
  'clips-screenshots': CHANNELS.CLIPS,
  memes: CHANNELS.MEMES,
  'self-promo': CHANNELS.SELF_PROMO,
  'music-commands': CHANNELS.MUSIC,
  'nsfw-uncensored': CHANNELS.NSFW,
  'content-creators': CHANNELS.CREATORS,
  'politics-and-irl': CHANNELS.POLITICS,
  'open-a-ticket': CHANNELS.TICKET,
  'mod-chat': CHANNELS.MOD_CHAT,
  'mod-logs': CHANNELS.MOD_LOGS,
  'ban-list': CHANNELS.BAN_LIST,
  'alt-flags': CHANNELS.ALT_FLAGS,
  'bot-commands': CHANNELS.BOT_COMMANDS,
  leaderboard: CHANNELS.LEADERBOARD,
  '🔊 Lounge': VOICE.LOUNGE,
  '🔊 Chill': VOICE.CHILL,
  'Squad 1': VOICE.SQUAD_1,
  'Squad 2': VOICE.SQUAD_2,
  Command: VOICE.COMMAND,
  '➕ Join to Create': VOICE.JOIN_TO_CREATE,
  '🔊 Other Games': VOICE.OTHER_GAMES,
  'Command Briefing': VOICE.BRIEFING,
  '💤 AFK': VOICE.AFK,
};

// Inside game categories (and OTHER GAMES) channels were "<game>-info",
// "<game>-chat" (one still carries a leftover "╠═" test prefix) and
// "<game>-squad" — matched by suffix, only within those categories.
const GAME_SUFFIX_RENAMES = [
  ['-info', CHANNELS.GAME_INFO],
  ['-chat', CHANNELS.GAME_CHAT],
  ['-squad', CHANNELS.GAME_SQUAD],
];

const OLD_LFG_TAGS = ['EU', 'NA', 'Casual', 'Hardcore', 'Need players', 'Full squad'];
const TAG_RENAMES = Object.fromEntries(OLD_LFG_TAGS.map((old, i) => [old, LFG_TAGS[i]]));

const OLD_RANKS = ['Newbie', 'Stalker', 'Veteran', 'Marksman', 'Trapper', 'Bandit Killer', 'Zone Master', 'Legend of the Zone'];
const RANK_RENAMES = Object.fromEntries(OLD_RANKS.map((old, i) => [old, RANK_LADDER[i]]));

const ROLE_RENAMES = {
  Owner: byKey(STAFF_ROLES, 'owner'),
  Admin: byKey(STAFF_ROLES, 'admin'),
  Moderator: byKey(STAFF_ROLES, 'moderator'),
  Helper: byKey(STAFF_ROLES, 'helper'),
  ...RANK_RENAMES,
  Politics: byKey(EXTRA_ROLES, 'politics'),
  'Other Games': byKey(EXTRA_ROLES, 'other-games'),
  'Content Creator': CONTENT_CREATOR_ROLE.name,
  'STAKEOUT FRIEND': BOOSTER_ROLE_NAME,
};

const AUTOMOD_RENAMES = {
  'STAKEOUT — Slurs & hate speech': 'STAKEOUT — Оскорбления и ненависть',
  'STAKEOUT — Mention spam': 'STAKEOUT — Спам упоминаниями',
  'STAKEOUT — Invite links': 'STAKEOUT — Ссылки-приглашения',
};

const rest = new REST({ version: '10' }).setToken(TOKEN);
const counts = { renamed: 0, skipped: 0 };

async function apply(label, oldName, newName, fn) {
  console.log(`  ${DRY_RUN ? '[plan]' : '✔'} ${label}: ${oldName} -> ${newName}`);
  counts.renamed += 1;
  if (!DRY_RUN) await fn();
}

async function renameChannel(channel, newName, label) {
  await apply(label, channel.name, newName, () =>
    rest.patch(Routes.channel(channel.id), { body: { name: newName }, reason: REASON }),
  );
}

async function main() {
  console.log(DRY_RUN ? '== DRY RUN — nothing will be changed ==\n' : '== APPLYING ==\n');
  const channels = await rest.get(Routes.guildChannels(GUILD_ID));
  const categoryById = new Map(channels.filter((c) => c.type === ChannelType.GuildCategory).map((c) => [c.id, c]));

  console.log('== categories ==');
  for (const category of categoryById.values()) {
    const newName = CATEGORY_RENAMES[category.name];
    if (newName) await renameChannel(category, newName, 'category');
  }

  // Resolve game/other-games categories by their *current* names — old
  // or new — so a partial earlier run still finds them.
  const gameCategoryNames = new Set([
    ...GAMES.map((g) => `${g.emoji} ${g.name}`),
    '🎲 OTHER GAMES',
    CATEGORIES.OTHER_GAMES,
  ]);
  const gameCategoryIds = new Set(
    [...categoryById.values()].filter((c) => gameCategoryNames.has(c.name)).map((c) => c.id),
  );

  console.log('== channels ==');
  for (const channel of channels) {
    if (channel.type === ChannelType.GuildCategory) continue;

    let newName = CHANNEL_RENAMES[channel.name];
    if (!newName && gameCategoryIds.has(channel.parent_id)) {
      const match = GAME_SUFFIX_RENAMES.find(([suffix]) => channel.name.endsWith(suffix));
      if (match) newName = match[1];
    }
    if (newName && newName !== channel.name) {
      const parent = categoryById.get(channel.parent_id)?.name ?? '—';
      await renameChannel(channel, newName, `channel in ${parent}`);
    }

    if (channel.type === ChannelType.GuildForum && channel.available_tags?.some((t) => TAG_RENAMES[t.name])) {
      const tags = channel.available_tags.map((t) => ({ ...t, name: TAG_RENAMES[t.name] ?? t.name }));
      await apply('forum tags', channel.available_tags.map((t) => t.name).join('/'), tags.map((t) => t.name).join('/'), () =>
        rest.patch(Routes.channel(channel.id), { body: { available_tags: tags }, reason: REASON }),
      );
    }
  }

  console.log('== roles ==');
  const roles = await rest.get(Routes.guildRoles(GUILD_ID));
  for (const role of roles) {
    const newName = ROLE_RENAMES[role.name];
    if (!newName) continue;
    if (role.managed) {
      console.log(`  ! skipped managed role: ${role.name}`);
      counts.skipped += 1;
      continue;
    }
    await apply('role', role.name, newName, () =>
      rest.patch(Routes.guildRole(GUILD_ID, role.id), { body: { name: newName }, reason: REASON }),
    );
  }

  console.log('== AutoMod rules ==');
  const rules = await rest.get(Routes.guildAutoModerationRules(GUILD_ID));
  for (const rule of rules) {
    const newName = AUTOMOD_RENAMES[rule.name];
    if (newName) {
      await apply('automod', rule.name, newName, () =>
        rest.patch(Routes.guildAutoModerationRule(GUILD_ID, rule.id), { body: { name: newName }, reason: REASON }),
      );
    }
  }

  console.log('== data/xp.json ranks ==');
  let xp = {};
  try {
    xp = JSON.parse(fs.readFileSync(XP_PATH, 'utf8'));
  } catch {
    console.log('  no xp.json — nothing to migrate');
  }
  let xpChanged = false;
  for (const [userId, entry] of Object.entries(xp)) {
    const newRank = RANK_RENAMES[entry.rank];
    if (newRank) {
      console.log(`  ${DRY_RUN ? '[plan]' : '✔'} ${userId}: ${entry.rank} -> ${newRank}`);
      entry.rank = newRank;
      xpChanged = true;
    }
  }
  if (xpChanged && !DRY_RUN) fs.writeFileSync(XP_PATH, JSON.stringify(xp, null, 2));

  console.log(`\n${DRY_RUN ? 'Planned' : 'Done'}: ${counts.renamed} rename(s), ${counts.skipped} skipped.`);
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
