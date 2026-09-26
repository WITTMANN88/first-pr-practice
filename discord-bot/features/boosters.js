// Booster perks: auto-granted badge role + thank-you in #welcome, plus
// two self-service perks — a custom name color (!color) and an XP
// multiplier (read by features/xp.js via BOOSTER_ROLE_NAME).
const { BOOSTER_ROLE_NAME: ROLE_NAME, CHANNELS } = require('../config');
const PREFIX = '!color';
const HEX_RE = /^#?([0-9a-fA-F]{6})$/;
const XP_MULTIPLIER = 1.5;

async function moveRoleNearTop(guild, role) {
  const me = await guild.members.fetchMe();
  const botTop = me.roles.highest;
  if (role.position >= botTop.position - 1) return;
  await role.setPosition(botTop.position - 1).catch(() => {});
}

async function ensureBoosterRole(guild) {
  let role = guild.roles.cache.find((r) => r.name === ROLE_NAME);
  if (!role) {
    role = await guild.roles.create({ name: ROLE_NAME, color: '#f47fff', hoist: true, mentionable: false });
    console.log(`+ role: ${ROLE_NAME}`);
  }
  await moveRoleNearTop(guild, role);
  return role;
}

function isBooster(member) {
  return member.roles.cache.some((r) => r.name === ROLE_NAME);
}

async function handleMemberUpdate(oldMember, newMember) {
  const startedBoosting = !oldMember.premiumSinceTimestamp && newMember.premiumSinceTimestamp;
  if (!startedBoosting) return;

  const role = await ensureBoosterRole(newMember.guild);
  await newMember.roles.add(role).catch(() => {});

  const welcome = newMember.guild.channels.cache.find((c) => c.name === CHANNELS.WELCOME);
  if (welcome?.isTextBased()) {
    await welcome
      .send(
        `🚀 <@${newMember.id}> забустил сервер — спасибо! Получена роль **${ROLE_NAME}**: свой цвет ника ` +
          `(\`!color #RRGGBB\`) и ×${XP_MULTIPLIER} к опыту и монетам за \`!daily\`.`,
      )
      .catch(() => {});
  }
}

async function handleColorCommand(message) {
  if (message.author.bot || !message.guild) return;
  if (!message.content.startsWith(PREFIX)) return;
  if (message.content.length > PREFIX.length && message.content[PREFIX.length] !== ' ') return;

  if (!isBooster(message.member)) {
    return void message.reply(`Эта команда только для бустеров сервера (роль ${ROLE_NAME}).`);
  }

  const hexArg = message.content.slice(PREFIX.length).trim();
  const match = HEX_RE.exec(hexArg);
  if (!match) {
    return void message.reply('Формат: `!color #RRGGBB`, например `!color #ff66aa`.');
  }
  const hex = `#${match[1]}`;
  const guild = message.guild;
  const roleName = `🎨 ${message.author.username}`.slice(0, 90);

  let role = message.member.roles.cache.find((r) => r.name.startsWith('🎨 '));
  if (!role) {
    role = await guild.roles.create({ name: roleName, color: hex, mentionable: false });
    await message.member.roles.add(role);
  } else {
    await role.edit({ color: hex, name: roleName });
  }
  await moveRoleNearTop(guild, role);
  message.reply(`🎨 Готово, твой цвет теперь ${hex}.`);
}

module.exports = { handleMemberUpdate, handleColorCommand, ensureBoosterRole, isBooster, ROLE_NAME, XP_MULTIPLIER };
