// Punishment commands — Admin + Moderator only (Helper doesn't get
// these). Text commands, no slash-command deploy needed.
const { addWarn, clearInfractions } = require('./infractions');
const tempbans = require('./tempbans');

const PREFIX = '!';
const PUNISH_ROLE_NAMES = ['Owner', 'Admin', 'Moderator'];
const COMMANDS = ['ban', 'tempban', 'unban', 'kick', 'mute', 'tempmute', 'unmute', 'warn', 'clear-all-infractions', 'clear'];
const DURATION_MULTIPLIERS = { s: 1000, m: 60_000, h: 3_600_000, d: 86_400_000 };
const MAX_TIMEOUT_MS = 28 * DURATION_MULTIPLIERS.d; // Discord's own timeout ceiling
const WARN_KICK_THRESHOLD = 3;
const WARN_BAN_THRESHOLD = 5;

function isPunishStaff(member) {
  return member.roles.cache.some((r) => PUNISH_ROLE_NAMES.includes(r.name));
}

function parseDuration(input) {
  const match = /^(\d+)(s|m|h|d)$/.exec(input?.trim() ?? '');
  if (!match) return null;
  return Number(match[1]) * DURATION_MULTIPLIERS[match[2]];
}

async function handleMessage(message) {
  if (message.author.bot || !message.guild) return;
  if (!message.content.startsWith(PREFIX)) return;

  const [cmdRaw, ...rest] = message.content.slice(PREFIX.length).trim().split(/\s+/);
  const cmd = cmdRaw?.toLowerCase();
  if (!COMMANDS.includes(cmd)) return;

  if (!isPunishStaff(message.member)) {
    return void message.reply('Эта команда только для Admin/Moderator.');
  }

  const target = message.mentions.members?.first();

  switch (cmd) {
    case 'ban': {
      if (!target) return void message.reply('Укажи участника: `!ban @user [причина]`');
      const reason = rest.slice(1).join(' ') || 'без причины';
      try {
        await target.ban({ reason });
        message.reply(`🔨 ${target.user.tag} забанен навсегда. Причина: ${reason}`);
      } catch (err) {
        message.reply(`Не вышло: ${err.message}`);
      }
      break;
    }
    case 'tempban': {
      if (!target) return void message.reply('Укажи участника: `!tempban @user <1d/2h> [причина]`');
      const durationMs = parseDuration(rest[1]);
      if (!durationMs) return void message.reply('Формат длительности: `10m`, `2h`, `1d`.');
      const reason = rest.slice(2).join(' ') || 'без причины';
      try {
        await target.ban({ reason: `[Временный, ${rest[1]}] ${reason}` });
        tempbans.schedule(message.guild.id, target.id, Date.now() + durationMs);
        message.reply(`⏳ ${target.user.tag} забанен на ${rest[1]}. Причина: ${reason}`);
      } catch (err) {
        message.reply(`Не вышло: ${err.message}`);
      }
      break;
    }
    case 'unban': {
      const userId = rest[0];
      if (!userId) return void message.reply('Укажи ID пользователя: `!unban 123456789012345678`');
      try {
        await message.guild.members.unban(userId, `Снято ${message.author.tag}`);
        tempbans.cancel(message.guild.id, userId);
        message.reply(`✅ Пользователь ${userId} разбанен.`);
      } catch (err) {
        message.reply(`Не вышло: ${err.message}`);
      }
      break;
    }
    case 'kick': {
      if (!target) return void message.reply('Укажи участника: `!kick @user [причина]`');
      const reason = rest.slice(1).join(' ') || 'без причины';
      try {
        await target.kick(reason);
        message.reply(`👢 ${target.user.tag} кикнут. Причина: ${reason}`);
      } catch (err) {
        message.reply(`Не вышло: ${err.message}`);
      }
      break;
    }
    case 'mute': {
      // "Indefinite" mute — Discord timeouts cap at 28 days, so this is
      // the longest single mute possible; re-run !mute to extend it.
      if (!target) return void message.reply('Укажи участника: `!mute @user [причина]`');
      const reason = rest.slice(1).join(' ') || 'без причины';
      try {
        await target.timeout(MAX_TIMEOUT_MS, reason);
        message.reply(`🔇 ${target.user.tag} замьючен (макс. срок — 28 дней, лимит самого Discord). Причина: ${reason}`);
      } catch (err) {
        message.reply(`Не вышло: ${err.message}`);
      }
      break;
    }
    case 'tempmute': {
      if (!target) return void message.reply('Укажи участника: `!tempmute @user <10m/1h/1d> [причина]`');
      const durationMs = parseDuration(rest[1]);
      if (!durationMs) return void message.reply('Формат длительности: `10m`, `2h`, `1d` (макс. 28 дней).');
      const reason = rest.slice(2).join(' ') || 'без причины';
      try {
        await target.timeout(durationMs, reason);
        message.reply(`🔇 ${target.user.tag} замьючен на ${rest[1]}. Причина: ${reason}`);
      } catch (err) {
        message.reply(`Не вышло: ${err.message}`);
      }
      break;
    }
    case 'unmute': {
      if (!target) return void message.reply('Укажи участника: `!unmute @user`');
      try {
        await target.timeout(null);
        message.reply(`🔊 ${target.user.tag} размьючен.`);
      } catch (err) {
        message.reply(`Не вышло: ${err.message}`);
      }
      break;
    }
    case 'warn': {
      if (!target) return void message.reply('Укажи участника: `!warn @user [причина]`');
      const reason = rest.slice(1).join(' ') || 'без причины';
      const count = addWarn(message.guild.id, target.id, { reason, moderatorTag: message.author.tag });
      await target.send(`⚠️ Тебе вынесено предупреждение на STAKEOUT (${count}-е). Причина: ${reason}`).catch(() => {});
      const modLogs = message.guild.channels.cache.find((c) => c.name === 'mod-logs');
      await modLogs
        ?.send(`⚠️ ${message.author.tag} выдал варн №${count} ${target.user.tag}: ${reason}`)
        .catch(() => {});
      message.reply(`⚠️ Варн №${count} выдан ${target.user.tag}.`);

      if (count === WARN_BAN_THRESHOLD) {
        await target.ban({ reason: `Автобан — ${WARN_BAN_THRESHOLD} варнов` }).catch(() => {});
        message.channel.send(`🔨 ${target.user.tag} автоматически забанен — достигнут лимит в ${WARN_BAN_THRESHOLD} варнов.`);
      } else if (count === WARN_KICK_THRESHOLD) {
        await target.kick(`Автокик — ${WARN_KICK_THRESHOLD} варна`).catch(() => {});
        message.channel.send(`👢 ${target.user.tag} автоматически кикнут — достигнут лимит в ${WARN_KICK_THRESHOLD} варна.`);
      }
      break;
    }
    case 'clear-all-infractions': {
      if (!target) return void message.reply('Укажи участника: `!clear-all-infractions @user`');
      clearInfractions(message.guild.id, target.id);
      message.reply(`🧹 История нарушений ${target.user.tag} полностью стёрта.`);
      break;
    }
    case 'clear': {
      const amount = Number(rest[0]);
      if (!amount || amount < 1 || amount > 100) return void message.reply('Укажи число от 1 до 100: `!clear 20`');
      const deleted = await message.channel.bulkDelete(amount + 1, true).catch(() => null);
      const notice = await message.channel.send(`🧹 Удалено ${deleted ? deleted.size - 1 : 0} сообщений.`);
      setTimeout(() => notice.delete().catch(() => {}), 5000);
      break;
    }
    default:
      break;
  }
}

module.exports = { handleMessage, isPunishStaff, PUNISH_ROLE_NAMES };
