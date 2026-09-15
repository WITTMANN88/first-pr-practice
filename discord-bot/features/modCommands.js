// Moderation commands, restricted to the Admin/Moderator roles —
// Helper doesn't get these. Text commands like music, no slash-command
// deploy needed: !kick, !ban, !mute, !unmute, !warn, !clear.
const PREFIX = '!';
const MOD_ROLE_NAMES = ['Admin', 'Moderator'];
const COMMANDS = ['kick', 'ban', 'mute', 'unmute', 'warn', 'clear'];
const DURATION_MULTIPLIERS = { s: 1000, m: 60_000, h: 3_600_000, d: 86_400_000 };

function isStaff(member) {
  return member.roles.cache.some((r) => MOD_ROLE_NAMES.includes(r.name));
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

  if (!isStaff(message.member)) {
    return void message.reply('Эта команда только для персонала.');
  }

  const target = message.mentions.members?.first();

  switch (cmd) {
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
    case 'ban': {
      if (!target) return void message.reply('Укажи участника: `!ban @user [причина]`');
      const reason = rest.slice(1).join(' ') || 'без причины';
      try {
        await target.ban({ reason });
        message.reply(`🔨 ${target.user.tag} забанен. Причина: ${reason}`);
      } catch (err) {
        message.reply(`Не вышло: ${err.message}`);
      }
      break;
    }
    case 'mute': {
      if (!target) return void message.reply('Укажи участника: `!mute @user <10m/1h/1d> [причина]`');
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
      await target.send(`⚠️ Тебе вынесено предупреждение на STAKEOUT. Причина: ${reason}`).catch(() => {});
      const modLogs = message.guild.channels.cache.find((c) => c.name === 'mod-logs');
      await modLogs?.send(`⚠️ ${message.author.tag} выдал варн ${target.user.tag}: ${reason}`).catch(() => {});
      message.reply(`⚠️ Варн выдан ${target.user.tag}.`);
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

module.exports = { handleMessage, isStaff };
