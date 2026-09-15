// Right-click a message -> "Пожаловаться" -> short modal -> private
// report channel, reusing the same Tickets category/permissions as
// the ticket system instead of building a parallel one.
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
  ApplicationCommandType,
  MessageFlags,
} = require('discord.js');
const { findOrCreateTicketsCategory, staffRoleIdsOf } = require('./tickets');
const COLORS = require('./colors');

const COMMAND_NAME = 'Пожаловаться';
// Modals can't carry full objects, only strings in the customId — stash
// the reported message's details here between the context-menu click
// and the modal submit, keyed by message id.
const pendingReports = new Map();

async function registerCommand(guild) {
  const commands = await guild.commands.fetch();
  const existing = commands.find((c) => c.name === COMMAND_NAME && c.type === ApplicationCommandType.Message);
  if (existing) return;
  await guild.commands.create({ name: COMMAND_NAME, type: ApplicationCommandType.Message });
  console.log(`+ context menu command: ${COMMAND_NAME}`);
}

async function handleContextMenu(interaction) {
  if (interaction.commandName !== COMMAND_NAME) return;

  const target = interaction.targetMessage;
  pendingReports.set(target.id, {
    content: target.content,
    authorTag: target.author.tag,
    authorId: target.author.id,
    channelId: target.channel.id,
    url: target.url,
  });

  const modal = new ModalBuilder().setCustomId(`report-modal-${target.id}`).setTitle('Пожаловаться на сообщение');
  const input = new TextInputBuilder()
    .setCustomId('reason')
    .setLabel('Почему жалуешься? (необязательно)')
    .setStyle(TextInputStyle.Paragraph)
    .setRequired(false)
    .setMaxLength(500);
  modal.addComponents(new ActionRowBuilder().addComponents(input));
  await interaction.showModal(modal);
}

async function handleModalSubmit(interaction) {
  const messageId = interaction.customId.replace('report-modal-', '');
  const info = pendingReports.get(messageId);
  pendingReports.delete(messageId);

  await interaction.deferReply({ flags: MessageFlags.Ephemeral });

  if (!info) {
    return void interaction.editReply('Не нашёл исходное сообщение — попробуй пожаловаться ещё раз.');
  }

  const reason = interaction.fields.getTextInputValue('reason') || 'не указана';
  const guild = interaction.guild;
  const category = await findOrCreateTicketsCategory(guild);

  const safeName = interaction.user.username.toLowerCase().replace(/[^a-z0-9]+/g, '-').slice(0, 20) || 'user';
  const channel = await guild.channels.create({
    name: `report-${safeName}`,
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
    .setTitle('🚩 Жалоба на сообщение')
    .addFields(
      { name: 'На кого', value: `<@${info.authorId}> (${info.authorTag})` },
      { name: 'Канал', value: `<#${info.channelId}>` },
      { name: 'Сообщение', value: info.content?.slice(0, 1024) || '_без текста (вложение/embed)_' },
      { name: 'Ссылка', value: info.url },
      { name: 'Причина', value: reason },
      { name: 'Пожаловался', value: `<@${interaction.user.id}>` },
    )
    .setColor(COLORS.WARNING)
    .setTimestamp();

  const closeRow = new ActionRowBuilder().addComponents(
    new ButtonBuilder().setCustomId('ticket-close').setLabel('Закрыть').setStyle(ButtonStyle.Danger).setEmoji('🔒'),
  );

  await channel.send({ embeds: [embed], components: [closeRow] });
  await interaction.editReply(`Жалоба отправлена: ${channel}`);
}

module.exports = { registerCommand, handleContextMenu, handleModalSubmit };
