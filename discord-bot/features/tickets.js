// Ticket system: button panel in #open-a-ticket -> modal for details ->
// private channel visible only to the opener and staff.
const {
  EmbedBuilder,
  ActionRowBuilder,
  ButtonBuilder,
  ButtonStyle,
  ModalBuilder,
  TextInputBuilder,
  TextInputStyle,
  ChannelType,
  PermissionFlagsBits,
  MessageFlags,
} = require('discord.js');
const { STAFF_ROLES } = require('../config');

const PANEL_MARKER = 'stakeout-ticket-panel-v1';
const TICKETS_CATEGORY_NAME = '🎫 Tickets';

const CATEGORIES = [
  { id: 'complaint', label: 'Жалоба', emoji: '⚠️', style: ButtonStyle.Danger },
  { id: 'appeal', label: 'Апелляция', emoji: '🔓', style: ButtonStyle.Secondary },
  { id: 'suggestion', label: 'Предложение', emoji: '💡', style: ButtonStyle.Success },
  { id: 'bug', label: 'Баг', emoji: '🐞', style: ButtonStyle.Primary },
];

function categoryLabel(id) {
  return CATEGORIES.find((c) => c.id === id)?.label ?? id;
}

function staffRoleIdsOf(guild) {
  return STAFF_ROLES.map((r) => guild.roles.cache.find((role) => role.name === r.name)?.id).filter(Boolean);
}

async function postTicketPanel(guild) {
  const channel = guild.channels.cache.find((c) => c.name === 'open-a-ticket');
  if (!channel) {
    console.warn('open-a-ticket channel not found — skipping ticket panel');
    return;
  }
  const recent = await channel.messages.fetch({ limit: 20 });
  const already = recent.find(
    (m) => m.author.id === guild.client.user.id && m.embeds[0]?.footer?.text === PANEL_MARKER,
  );
  if (already) return;

  const embed = new EmbedBuilder()
    .setTitle('Открыть тикет')
    .setDescription('Выбери категорию — откроется приватный канал, который видишь только ты и персонал.')
    .setColor(0x8b0000)
    .setFooter({ text: PANEL_MARKER });

  const row = new ActionRowBuilder().addComponents(
    CATEGORIES.map((c) =>
      new ButtonBuilder().setCustomId(`ticket-open-${c.id}`).setLabel(c.label).setEmoji(c.emoji).setStyle(c.style),
    ),
  );

  await channel.send({ embeds: [embed], components: [row] });
  console.log('Posted ticket panel in #open-a-ticket');
}

async function handleTicketButton(interaction) {
  const categoryId = interaction.customId.replace('ticket-open-', '');
  const modal = new ModalBuilder()
    .setCustomId(`ticket-modal-${categoryId}`)
    .setTitle(`Тикет: ${categoryLabel(categoryId)}`);

  const input = new TextInputBuilder()
    .setCustomId('description')
    .setLabel('Опиши ситуацию')
    .setStyle(TextInputStyle.Paragraph)
    .setRequired(true)
    .setMaxLength(1000);

  modal.addComponents(new ActionRowBuilder().addComponents(input));
  await interaction.showModal(modal);
}

async function findOrCreateTicketsCategory(guild) {
  const existing = guild.channels.cache.find(
    (c) => c.type === ChannelType.GuildCategory && c.name === TICKETS_CATEGORY_NAME,
  );
  if (existing) return existing;

  return guild.channels.create({
    name: TICKETS_CATEGORY_NAME,
    type: ChannelType.GuildCategory,
    permissionOverwrites: [
      { id: guild.roles.everyone.id, deny: [PermissionFlagsBits.ViewChannel] },
      ...staffRoleIdsOf(guild).map((id) => ({ id, allow: [PermissionFlagsBits.ViewChannel] })),
    ],
  });
}

async function handleTicketModalSubmit(interaction) {
  await interaction.deferReply({ flags: MessageFlags.Ephemeral });

  const categoryId = interaction.customId.replace('ticket-modal-', '');
  const description = interaction.fields.getTextInputValue('description');
  const guild = interaction.guild;
  const category = await findOrCreateTicketsCategory(guild);

  const safeName = interaction.user.username.toLowerCase().replace(/[^a-z0-9]+/g, '-').slice(0, 20) || 'user';
  const channel = await guild.channels.create({
    name: `${categoryId}-${safeName}`,
    type: ChannelType.GuildText,
    parent: category.id,
    permissionOverwrites: [
      { id: guild.roles.everyone.id, deny: [PermissionFlagsBits.ViewChannel] },
      { id: interaction.user.id, allow: [PermissionFlagsBits.ViewChannel, PermissionFlagsBits.SendMessages] },
      ...staffRoleIdsOf(guild).map((id) => ({
        id,
        allow: [PermissionFlagsBits.ViewChannel, PermissionFlagsBits.SendMessages],
      })),
    ],
  });

  const embed = new EmbedBuilder()
    .setTitle(`Тикет — ${categoryLabel(categoryId)}`)
    .setDescription(description)
    .addFields({ name: 'Открыл', value: `<@${interaction.user.id}>` })
    .setColor(0x8b0000)
    .setTimestamp();

  const closeRow = new ActionRowBuilder().addComponents(
    new ButtonBuilder().setCustomId('ticket-close').setLabel('Закрыть тикет').setStyle(ButtonStyle.Danger).setEmoji('🔒'),
  );

  await channel.send({ content: `<@${interaction.user.id}>`, embeds: [embed], components: [closeRow] });
  await interaction.editReply(`Тикет создан: ${channel}`);
}

async function handleTicketClose(interaction) {
  await interaction.reply('Тикет закрывается через 5 секунд...');
  setTimeout(() => interaction.channel.delete().catch(() => {}), 5000);
}

module.exports = { postTicketPanel, handleTicketButton, handleTicketModalSubmit, handleTicketClose };
