// XP system: earns from text messages (cooldown, to stop spam-farming)
// and from time spent in voice (ticked every minute). Crossing a
// threshold swaps the member's rank role and announces it in #general.
const fs = require('fs');
const path = require('path');
const { RANK_LADDER, RANK_THRESHOLDS } = require('../config');

const DATA_PATH = path.join(__dirname, '..', 'data', 'xp.json');
const MESSAGE_COOLDOWN_MS = 60_000;
const MESSAGE_XP_MIN = 10;
const MESSAGE_XP_MAX = 20;
const VOICE_TICK_MS = 60_000;
const VOICE_XP_PER_TICK = 5;

let store = {};
const lastMessageAt = new Map();

function load() {
  try {
    store = JSON.parse(fs.readFileSync(DATA_PATH, 'utf8'));
  } catch {
    store = {};
  }
}

function save() {
  fs.mkdirSync(path.dirname(DATA_PATH), { recursive: true });
  fs.writeFileSync(DATA_PATH, JSON.stringify(store, null, 2));
}

function rankNameForXp(xp) {
  let idx = 0;
  for (let i = 0; i < RANK_THRESHOLDS.length; i++) {
    if (xp >= RANK_THRESHOLDS[i]) idx = i;
  }
  return RANK_LADDER[idx];
}

async function swapRankRole(member, oldRankName, newRankName) {
  const guild = member.guild;
  const oldRole = guild.roles.cache.find((r) => r.name === oldRankName);
  const newRole = guild.roles.cache.find((r) => r.name === newRankName);
  if (oldRole && member.roles.cache.has(oldRole.id)) {
    await member.roles.remove(oldRole).catch(() => {});
  }
  if (newRole && !member.roles.cache.has(newRole.id)) {
    await member.roles.add(newRole).catch(() => {});
  }
  const general = guild.channels.cache.find((c) => c.name === 'general');
  if (general?.isTextBased()) {
    general.send(`🎖️ <@${member.id}> получает новое звание — **${newRankName}**!`).catch(() => {});
  }
}

async function grantXp(member, amount) {
  const entry = store[member.id] ?? { xp: 0, rank: RANK_LADDER[0] };
  entry.xp += amount;
  const newRank = rankNameForXp(entry.xp);
  const oldRank = entry.rank;
  entry.rank = newRank;
  store[member.id] = entry;
  save();

  if (newRank !== oldRank) {
    await swapRankRole(member, oldRank, newRank);
  }
}

function handleMessage(message) {
  if (message.author.bot || !message.guild) return;
  const userId = message.author.id;
  const now = Date.now();
  const last = lastMessageAt.get(userId) ?? 0;
  if (now - last < MESSAGE_COOLDOWN_MS) return;
  lastMessageAt.set(userId, now);

  const amount = MESSAGE_XP_MIN + Math.floor(Math.random() * (MESSAGE_XP_MAX - MESSAGE_XP_MIN + 1));
  message.member ? grantXp(message.member, amount).catch(() => {}) : null;
}

function startVoiceTicker(client) {
  setInterval(() => {
    for (const guild of client.guilds.cache.values()) {
      for (const channel of guild.channels.cache.values()) {
        if (!channel.isVoiceBased() || channel.name.toUpperCase().includes('AFK')) continue;
        for (const member of channel.members.values()) {
          if (member.user.bot) continue;
          grantXp(member, VOICE_XP_PER_TICK).catch(() => {});
        }
      }
    }
  }, VOICE_TICK_MS);
}

function getProfile(userId) {
  return store[userId] ?? { xp: 0, rank: RANK_LADDER[0] };
}

load();

module.exports = { handleMessage, startVoiceTicker, getProfile };
