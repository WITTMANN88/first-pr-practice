// Rotating bot status — shown under the bot's name in the member list.
// Not the same thing as Discord's Game SDK "Rich Presence" (that's for
// a native game client running on someone's PC, not a server bot) —
// this is the simple Gateway activity every bot can set.
const { ActivityType } = require('discord.js');
const { GAMES, CHANNELS } = require('../config');

const ROTATE_MS = 45_000;

function buildStatuses(guild) {
  const randomGame = GAMES[Math.floor(Math.random() * GAMES.length)];
  return [
    { name: 'за STAKEOUT', type: ActivityType.Watching },
    { name: `за ${guild.memberCount} сталкерами`, type: ActivityType.Watching },
    { name: `!play в #${CHANNELS.MUSIC}`, type: ActivityType.Listening },
    { name: randomGame.name, type: ActivityType.Playing },
    { name: 'Зоне', type: ActivityType.Competing },
  ];
}

function startPresenceRotation(client, guildId) {
  let index = 0;

  const tick = async () => {
    try {
      const guild = await client.guilds.fetch(guildId);
      const statuses = buildStatuses(guild);
      const activity = statuses[index % statuses.length];
      index += 1;

      client.user.setPresence({ activities: [activity], status: 'online' });
    } catch (err) {
      console.error('Presence rotation failed:', err);
    }
  };

  tick();
  setInterval(tick, ROTATE_MS);
}

module.exports = { startPresenceRotation };
