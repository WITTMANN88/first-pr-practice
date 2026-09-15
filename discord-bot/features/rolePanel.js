// Posts a self-service role menu in #choose-your-roles and keeps a
// member's access roles in sync with what they pick.
const { EmbedBuilder, ActionRowBuilder, StringSelectMenuBuilder, MessageFlags } = require('discord.js');
const { GAMES, EXTRA_ROLES } = require('../config');
const { upsertPanel } = require('./messageRegistry');

function gamesSelectMenu() {
  return new StringSelectMenuBuilder()
    .setCustomId('roles-games')
    .setPlaceholder('Выбери игры, в которые играешь')
    .setMinValues(0)
    .setMaxValues(GAMES.length)
    .addOptions(GAMES.map((g) => ({ label: g.name, value: g.name, emoji: g.emoji })));
}

function extraSelectMenu() {
  return new StringSelectMenuBuilder()
    .setCustomId('roles-extra')
    .setPlaceholder('Дополнительные роли')
    .setMinValues(0)
    .setMaxValues(EXTRA_ROLES.length)
    .addOptions(
      EXTRA_ROLES.map((r) => ({ label: r.name, value: r.name, emoji: r.emoji, description: r.description })),
    );
}

async function registerRolePanel(guild) {
  const channel = guild.channels.cache.find((c) => c.name === 'choose-your-roles');
  if (!channel) {
    console.warn('choose-your-roles channel not found — skipping role panel');
    return;
  }

  const embed = new EmbedBuilder()
    .setTitle('Выбери роли / Choose your roles')
    .setDescription(
      'Каждая роль открывает свою категорию каналов — пока роль не взята, категории не видно вообще. ' +
        'Ничего страшного, если передумаешь: можно менять выбор в любой момент.\n\n' +
        '**Игры** — открывает чат, LFG-форум и голосовые той игры.\n' +
        '**Дополнительно** — Politics открывает SERIOUS TALK, Other Games — категорию для всего остального.\n\n' +
        '— — —\n\n' +
        "Each role unlocks its own category of channels — until you take the role, the category isn't visible at all. " +
        'No worries if you change your mind: you can update your picks any time.\n\n' +
        '**Games** — unlocks the chat, LFG forum and voice channels for that game.\n' +
        '**Extra** — Politics unlocks SERIOUS TALK, Other Games unlocks the catch-all category for everything else.',
    )
    .setColor(0x8b0000);

  await upsertPanel(channel, 'role-panel', {
    embeds: [embed],
    components: [
      new ActionRowBuilder().addComponents(gamesSelectMenu()),
      new ActionRowBuilder().addComponents(extraSelectMenu()),
    ],
  });
  console.log('Role panel synced in #choose-your-roles');
}

async function handleRoleSelect(interaction) {
  const isGames = interaction.customId === 'roles-games';
  const isExtra = interaction.customId === 'roles-extra';
  if (!isGames && !isExtra) return;

  await interaction.deferReply({ flags: MessageFlags.Ephemeral });

  const pool = (isGames ? GAMES : EXTRA_ROLES).map((r) => r.name);
  const selected = new Set(interaction.values);
  const member = await interaction.guild.members.fetch(interaction.user.id);

  const toAdd = [];
  const toRemove = [];
  for (const roleName of pool) {
    const role = interaction.guild.roles.cache.find((r) => r.name === roleName);
    if (!role) continue;
    const has = member.roles.cache.has(role.id);
    const wants = selected.has(roleName);
    if (wants && !has) toAdd.push(role);
    if (!wants && has) toRemove.push(role);
  }

  for (const role of toAdd) await member.roles.add(role);
  for (const role of toRemove) await member.roles.remove(role);

  const parts = [];
  if (toAdd.length) parts.push(`выдано: ${toAdd.map((r) => r.name).join(', ')}`);
  if (toRemove.length) parts.push(`снято: ${toRemove.map((r) => r.name).join(', ')}`);

  await interaction.editReply(parts.length ? parts.join(' · ') : 'Без изменений — уже так было настроено.');
}

module.exports = { registerRolePanel, handleRoleSelect };
