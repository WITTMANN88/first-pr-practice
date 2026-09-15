// One-off moderation utility: wipes all messages in a channel by name.
// Usage: node scripts/purge-channel.js <channel-name>
// Discord only allows bulk-deleting messages younger than 14 days —
// older ones are skipped and reported separately.
require('dotenv').config();
const { Client, GatewayIntentBits } = require('discord.js');

const TOKEN = process.env.DISCORD_TOKEN;
const GUILD_ID = process.env.GUILD_ID;
const channelName = process.argv[2];

if (!TOKEN || !GUILD_ID) {
  console.error('Missing DISCORD_TOKEN or GUILD_ID in .env');
  process.exit(1);
}
if (!channelName) {
  console.error('Usage: node scripts/purge-channel.js <channel-name>');
  process.exit(1);
}

const client = new Client({ intents: [GatewayIntentBits.Guilds, GatewayIntentBits.GuildMessages] });

client.once('clientReady', async () => {
  try {
    const guild = await client.guilds.fetch(GUILD_ID);
    const channel = guild.channels.cache.find((c) => c.name === channelName && c.isTextBased());
    if (!channel) {
      console.error(`No text channel named "${channelName}" found.`);
      process.exit(1);
    }

    let totalDeleted = 0;
    let skippedOld = 0;
    for (;;) {
      const messages = await channel.messages.fetch({ limit: 100 });
      if (messages.size === 0) break;
      const deleted = await channel.bulkDelete(messages, true);
      totalDeleted += deleted.size;
      if (deleted.size < messages.size) {
        skippedOld += messages.size - deleted.size;
        break; // remaining messages are all >14 days old, bulkDelete won't touch them
      }
    }

    console.log(`Deleted ${totalDeleted} message(s) from #${channelName}.`);
    if (skippedOld > 0) {
      console.log(`Skipped ${skippedOld} message(s) older than 14 days (Discord API limit).`);
    }
    process.exit(0);
  } catch (err) {
    console.error(err);
    process.exit(1);
  }
});

client.login(TOKEN);
