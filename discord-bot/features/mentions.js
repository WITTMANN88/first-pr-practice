// Renders a channel as a clickable mention when it exists, so text that
// points people somewhere stays correct even if the channel is renamed.
function channelRef(guild, name) {
  const channel = guild.channels.cache.find((c) => c.name === name);
  return channel ? `<#${channel.id}>` : `#${name}`;
}

module.exports = { channelRef };
