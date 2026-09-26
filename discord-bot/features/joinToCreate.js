// Join-to-Create: joining the "➕ Создать канал" voice channel spawns a
// fresh personal voice channel in the same category and moves the member
// into it; the temp channel is deleted once it's empty again. Only
// channels this module created are ever auto-deleted — Отряд 1/2/Штаб
// and the trigger channel itself are never touched.
const { ChannelType, PermissionFlagsBits } = require('discord.js');
const { VOICE } = require('../config');

const TRIGGER_NAME = VOICE.JOIN_TO_CREATE;
const createdChannels = new Set();

async function handleVoiceStateUpdate(oldState, newState) {
  if (newState.channel?.name === TRIGGER_NAME) {
    const guild = newState.guild;
    const member = newState.member;
    const category = newState.channel.parent;

    const channel = await guild.channels.create({
      name: `🔊 Отряд ${member.displayName}`.slice(0, 95),
      type: ChannelType.GuildVoice,
      parent: category?.id ?? undefined,
      permissionOverwrites: [
        {
          id: member.id,
          allow: [PermissionFlagsBits.ManageChannels, PermissionFlagsBits.MoveMembers],
        },
      ],
    });

    createdChannels.add(channel.id);
    await newState.setChannel(channel).catch(() => {});
  }

  const left = oldState.channel;
  if (left && createdChannels.has(left.id) && left.members.size === 0) {
    createdChannels.delete(left.id);
    await left.delete().catch(() => {});
  }
}

module.exports = { handleVoiceStateUpdate };
