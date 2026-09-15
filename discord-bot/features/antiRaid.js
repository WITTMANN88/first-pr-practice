// Anti-raid: flags freshly-created accounts on join, and separately
// alerts staff when joins spike in a short window (raid pattern).
// Deliberately doesn't auto-kick/ban or touch server settings — it
// surfaces suspicious activity in #alt-flags for a human to act on.
const { EmbedBuilder } = require('discord.js');
const { STAFF_ROLES } = require('../config');
const COLORS = require('./colors');

const NEW_ACCOUNT_THRESHOLD_MS = 7 * 24 * 60 * 60 * 1000; // 7 days
const RAID_WINDOW_MS = 10_000;
const RAID_JOIN_THRESHOLD = 5;
const RAID_ALERT_COOLDOWN_MS = 60_000;

const recentJoins = [];
let lastRaidAlertAt = 0;

function alertChannel(guild) {
  return guild.channels.cache.find((c) => c.name === 'alt-flags');
}

function staffMention(guild) {
  return STAFF_ROLES.map((r) => guild.roles.cache.find((role) => role.name === r.name))
    .filter(Boolean)
    .map((role) => `<@&${role.id}>`)
    .join(' ');
}

async function checkNewAccount(member) {
  const age = Date.now() - member.user.createdTimestamp;
  if (age >= NEW_ACCOUNT_THRESHOLD_MS) return;

  const channel = alertChannel(member.guild);
  if (!channel?.isTextBased()) return;

  const days = Math.floor(age / (24 * 60 * 60 * 1000));
  const embed = new EmbedBuilder()
    .setTitle('⚠️ Свежий аккаунт')
    .setDescription(`<@${member.id}> — аккаунту ${days === 0 ? 'меньше суток' : `${days} дн.`}`)
    .addFields({ name: 'Создан', value: `<t:${Math.floor(member.user.createdTimestamp / 1000)}:F>` })
    .setColor(COLORS.WARNING)
    .setTimestamp();

  await channel.send({ embeds: [embed] }).catch(() => {});
}

async function checkRaidBurst(member) {
  const now = Date.now();
  recentJoins.push(now);
  while (recentJoins.length && now - recentJoins[0] > RAID_WINDOW_MS) {
    recentJoins.shift();
  }
  if (recentJoins.length < RAID_JOIN_THRESHOLD) return;
  if (now - lastRaidAlertAt < RAID_ALERT_COOLDOWN_MS) return;
  lastRaidAlertAt = now;

  const channel = alertChannel(member.guild);
  if (!channel?.isTextBased()) return;

  await channel
    .send({
      content: staffMention(member.guild),
      embeds: [
        new EmbedBuilder()
          .setTitle('🚨 Похоже на рейд')
          .setDescription(`${recentJoins.length} входов за последние ${RAID_WINDOW_MS / 1000}с. Проверьте вручную.`)
          .setColor(COLORS.DANGER)
          .setTimestamp(),
      ],
    })
    .catch(() => {});
}

async function handleMemberAdd(member) {
  await checkNewAccount(member);
  await checkRaidBurst(member);
}

module.exports = { handleMemberAdd };
