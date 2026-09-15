// Logs manual message deletes/edits to #mod-logs — the gap AutoMod
// doesn't cover (it only sees what it blocks itself). Only catches
// messages the bot saw while running (Discord.js message cache), not
// history from before it started or restarts.
const { EmbedBuilder } = require('discord.js');
const COLORS = require('./colors');

function logChannel(guild) {
  return guild.channels.cache.find((c) => c.name === 'mod-logs');
}

async function handleDelete(message) {
  if (!message.guild || message.author?.bot) return;
  const channel = logChannel(message.guild);
  if (!channel?.isTextBased()) return;

  const embed = new EmbedBuilder()
    .setTitle('🗑️ Сообщение удалено')
    .setDescription(message.content || '_без текста (вложение/embed)_')
    .addFields(
      { name: 'Автор', value: message.author ? `<@${message.author.id}>` : 'неизвестно', inline: true },
      { name: 'Канал', value: `<#${message.channel.id}>`, inline: true },
    )
    .setColor(COLORS.DANGER)
    .setTimestamp();

  await channel.send({ embeds: [embed] }).catch(() => {});
}

async function handleEdit(oldMessage, newMessage) {
  if (!newMessage.guild || newMessage.author?.bot) return;
  if (oldMessage.content === newMessage.content) return;
  const channel = logChannel(newMessage.guild);
  if (!channel?.isTextBased()) return;

  const embed = new EmbedBuilder()
    .setTitle('✏️ Сообщение отредактировано')
    .addFields(
      { name: 'Автор', value: `<@${newMessage.author.id}>`, inline: true },
      { name: 'Канал', value: `<#${newMessage.channel.id}>`, inline: true },
      { name: 'Было', value: (oldMessage.content || '_пусто_').slice(0, 1024) },
      { name: 'Стало', value: (newMessage.content || '_пусто_').slice(0, 1024) },
    )
    .setColor(COLORS.WARNING)
    .setTimestamp();

  await channel.send({ embeds: [embed] }).catch(() => {});
}

module.exports = { handleDelete, handleEdit };
