// Read-only staff commands — Admin/Moderator/Helper all get these
// (nothing here changes state on a member, aside from slowmode on a
// channel). Text commands, no slash-command deploy needed.
const { EmbedBuilder } = require('discord.js');
const { getInfractions } = require('./infractions');
const { STAFF_ROLES } = require('../config');

const PREFIX = '!';
const STAFF_ROLE_NAMES = STAFF_ROLES.map((r) => r.name);
const COMMANDS = ['infractions', 'slowmode', 'user-info', 'server-info', 'role-info'];
const DURATION_MULTIPLIERS = { s: 1, m: 60, h: 3600, d: 86400 };
const MAX_SLOWMODE_SECONDS = 21_600; // 6h, Discord's own ceiling

function isStaff(member) {
  return member.roles.cache.some((r) => STAFF_ROLE_NAMES.includes(r.name));
}

function parseSeconds(input) {
  if (input === 'off' || input === '0') return 0;
  const match = /^(\d+)(s|m|h)$/.exec(input?.trim() ?? '');
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

  switch (cmd) {
    case 'infractions': {
      const target = message.mentions.members?.first();
      if (!target) return void message.reply('Укажи участника: `!infractions @user`');
      const list = getInfractions(message.guild.id, target.id);
      if (!list.length) return void message.reply(`У ${target.user.tag} нет нарушений.`);
      const embed = new EmbedBuilder()
        .setTitle(`Нарушения — ${target.user.tag}`)
        .setDescription(
          list
            .map((w, i) => `**${i + 1}.** ${w.reason} — <t:${Math.floor(w.at / 1000)}:d> (выдал ${w.moderatorTag})`)
            .join('\n'),
        )
        .setColor(0x8b0000);
      message.reply({ embeds: [embed] });
      break;
    }
    case 'slowmode': {
      const seconds = parseSeconds(rest[0]);
      if (seconds === null || seconds > MAX_SLOWMODE_SECONDS) {
        return void message.reply('Формат: `!slowmode 10s`, `!slowmode 5m`, `!slowmode off` (макс. 6ч).');
      }
      await message.channel.setRateLimitPerUser(seconds);
      message.reply(seconds === 0 ? '🐇 Slowmode выключен.' : `🐌 Slowmode: 1 сообщение в ${rest[0]}.`);
      break;
    }
    case 'user-info': {
      const target = message.mentions.members?.first() ?? message.member;
      const roles = target.roles.cache.filter((r) => r.id !== message.guild.id).map((r) => `<@&${r.id}>`);
      const embed = new EmbedBuilder()
        .setTitle(target.user.tag)
        .setThumbnail(target.user.displayAvatarURL())
        .addFields(
          { name: 'ID', value: target.id, inline: true },
          { name: 'Аккаунт создан', value: `<t:${Math.floor(target.user.createdTimestamp / 1000)}:D>`, inline: true },
          {
            name: 'На сервере с',
            value: target.joinedTimestamp ? `<t:${Math.floor(target.joinedTimestamp / 1000)}:D>` : 'неизвестно',
            inline: true,
          },
          { name: `Роли (${roles.length})`, value: roles.join(', ') || '_нет_' },
        )
        .setColor(0x8b0000);
      message.reply({ embeds: [embed] });
      break;
    }
    case 'server-info': {
      const guild = message.guild;
      const embed = new EmbedBuilder()
        .setTitle(guild.name)
        .setThumbnail(guild.iconURL())
        .addFields(
          { name: 'Участников', value: String(guild.memberCount), inline: true },
          { name: 'Ролей', value: String(guild.roles.cache.size), inline: true },
          { name: 'Каналов', value: String(guild.channels.cache.size), inline: true },
          { name: 'Бустов', value: `${guild.premiumSubscriptionCount ?? 0} (уровень ${guild.premiumTier})`, inline: true },
          { name: 'Создан', value: `<t:${Math.floor(guild.createdTimestamp / 1000)}:D>`, inline: true },
          { name: 'Владелец', value: `<@${guild.ownerId}>`, inline: true },
        )
        .setColor(0x8b0000);
      message.reply({ embeds: [embed] });
      break;
    }
    case 'role-info': {
      const name = rest.join(' ');
      if (!name) return void message.reply('Укажи роль: `!role-info Operator`');
      const role = message.guild.roles.cache.find((r) => r.name.toLowerCase() === name.toLowerCase());
      if (!role) return void message.reply(`Роль "${name}" не найдена.`);
      const embed = new EmbedBuilder()
        .setTitle(role.name)
        .addFields(
          { name: 'ID', value: role.id, inline: true },
          { name: 'Цвет', value: role.hexColor, inline: true },
          { name: 'Участников', value: String(role.members.size), inline: true },
          { name: 'Позиция', value: String(role.position), inline: true },
          { name: 'Упоминаемая', value: role.mentionable ? 'да' : 'нет', inline: true },
          { name: 'Отдельно в списке', value: role.hoist ? 'да' : 'нет', inline: true },
        )
        .setColor(role.color || 0x8b0000);
      message.reply({ embeds: [embed] });
      break;
    }
    default:
      break;
  }
}

module.exports = { handleMessage, isStaff };
