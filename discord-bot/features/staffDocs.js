// Re-asserts the staff category lock (exactly the staff roles, nobody
// else) and posts a command reference inside it.
const { ChannelType, EmbedBuilder, PermissionFlagsBits } = require('discord.js');
const { STAFF_ROLES, CATEGORIES, CHANNELS } = require('../config');
const { upsertPanel } = require('./messageRegistry');
const COLORS = require('./colors');

const STAFF_CATEGORY_NAME = CATEGORIES.STAFF;

async function lockStaffOnlyCategory(guild) {
  const category = guild.channels.cache.find(
    (c) => c.type === ChannelType.GuildCategory && c.name === STAFF_CATEGORY_NAME,
  );
  if (!category) {
    console.warn('STAFF ONLY category not found — skipping lock');
    return;
  }

  const staffRoleIds = STAFF_ROLES.map((r) => guild.roles.cache.find((role) => role.name === r.name)?.id).filter(
    Boolean,
  );

  await category.permissionOverwrites.set([
    { id: guild.roles.everyone.id, deny: [PermissionFlagsBits.ViewChannel] },
    ...staffRoleIds.map((id) => ({ id, allow: [PermissionFlagsBits.ViewChannel] })),
  ]);
  console.log('STAFF ONLY locked to:', STAFF_ROLES.map((r) => r.name).join(', '));
}

async function postCommandReference(guild) {
  const category = guild.channels.cache.find(
    (c) => c.type === ChannelType.GuildCategory && c.name === STAFF_CATEGORY_NAME,
  );
  if (!category) return;

  let channel = guild.channels.cache.find((c) => c.name === CHANNELS.BOT_COMMANDS && c.parentId === category.id);
  if (!channel) {
    channel = await guild.channels.create({ name: CHANNELS.BOT_COMMANDS, type: ChannelType.GuildText, parent: category.id });
    console.log('+ channel: bot-commands');
  }

  const punishEmbed = new EmbedBuilder()
    .setTitle('🚫 Наказания и блокировки — модераторы и выше')
    .setDescription(
      [
        '`!ban @user [причина]` — забанить навсегда.',
        '`!tempban @user <1d/2h> [причина]` — забанить на срок, разбан автоматический.',
        '`!unban <ID>` — разбанить по ID.',
        '`!kick @user [причина]` — кикнуть.',
        '`!mute @user [причина]` — замьютить на максимум (28 дней — лимит самого Discord).',
        '`!tempmute @user <10m/1h/1d> [причина]` — замьютить на срок.',
        '`!unmute @user` — снять мут.',
        `\`!warn @user [причина]\` — выдать варн. При ${'`3`'} — автокик, при ${'`5`'} — автобан.`,
      ].join('\n'),
    )
    .setColor(COLORS.BRAND);

  const cleanupEmbed = new EmbedBuilder()
    .setTitle('🧹 Очистка и нарушения — модераторы и выше')
    .setDescription(
      [
        '`!clear <число>` — удалить N сообщений в текущем канале (1-100).',
        '`!clear-all-infractions @user` — стереть всю историю варнов участника.',
      ].join('\n'),
    )
    .setColor(COLORS.BRAND);

  const infoEmbed = new EmbedBuilder()
    .setTitle('⚙️ Настройки и информация — весь персонал')
    .setDescription(
      [
        '`!infractions @user` — история варнов участника.',
        '`!slowmode <10s/5m/off>` — медленный режим в текущем канале (макс. 6ч).',
        '`!user-info [@user]` — дата регистрации, дата входа, роли.',
        '`!server-info` — статистика сервера.',
        '`!role-info <название роли>` — цвет, число участников, права роли.',
      ].join('\n'),
    )
    .setColor(COLORS.BRAND);

  await upsertPanel(channel, 'staff-commands', { embeds: [punishEmbed, cleanupEmbed, infoEmbed] });
  console.log('Command reference synced in #bot-commands');
}

module.exports = { lockStaffOnlyCategory, postCommandReference };
