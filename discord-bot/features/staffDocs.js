// Re-asserts the STAFF ONLY category lock (exactly Admin/Moderator/
// Helper, nobody else) and posts a command reference inside it —
// idempotent, same footer-marker pattern as the other panels.
const { ChannelType, EmbedBuilder, PermissionFlagsBits } = require('discord.js');
const { STAFF_ROLES } = require('../config');

const MARKER = 'stakeout-staff-commands-v1';
const STAFF_CATEGORY_NAME = '🔐 STAFF ONLY';

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

  let channel = guild.channels.cache.find((c) => c.name === 'bot-commands' && c.parentId === category.id);
  if (!channel) {
    channel = await guild.channels.create({ name: 'bot-commands', type: ChannelType.GuildText, parent: category.id });
    console.log('+ channel: bot-commands');
  }

  const recent = await channel.messages.fetch({ limit: 20 });
  const already = recent.find(
    (m) => m.author.id === guild.client.user.id && m.embeds.some((e) => e.footer?.text === MARKER),
  );
  if (already) return;

  const punishEmbed = new EmbedBuilder()
    .setTitle('🚫 Наказания и блокировки — Admin / Moderator')
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
    .setColor(0x8b0000);

  const cleanupEmbed = new EmbedBuilder()
    .setTitle('🧹 Очистка и нарушения — Admin / Moderator')
    .setDescription(
      [
        '`!clear <число>` — удалить N сообщений в текущем канале (1-100).',
        '`!clear-all-infractions @user` — стереть всю историю варнов участника.',
      ].join('\n'),
    )
    .setColor(0x8b0000);

  const infoEmbed = new EmbedBuilder()
    .setTitle('⚙️ Настройки и информация — Admin / Moderator / Helper')
    .setDescription(
      [
        '`!infractions @user` — история варнов участника.',
        '`!slowmode <10s/5m/off>` — slowmode в текущем канале (макс. 6ч).',
        '`!user-info [@user]` — дата регистрации, дата входа, роли.',
        '`!server-info` — статистика сервера.',
        '`!role-info <название роли>` — цвет, число участников, права роли.',
      ].join('\n'),
    )
    .setColor(0x8b0000)
    .setFooter({ text: MARKER });

  await channel.send({ embeds: [punishEmbed, cleanupEmbed, infoEmbed] });
  console.log('Posted command reference in #bot-commands');
}

module.exports = { lockStaffOnlyCategory, postCommandReference };
