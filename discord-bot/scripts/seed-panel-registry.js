// One-off migration: the panel-messages.json registry (features/messageRegistry.js)
// starts empty, so without this, upsertPanel would not recognize the old
// marker-footer messages already live in the channels and would post fresh
// duplicates instead of editing them in place. This scans each known panel
// channel for the bot's own message carrying the old marker text, and seeds
// the registry with its message ID so the next sync edits that same message
// (stripping the marker) rather than posting a new one. Safe to re-run —
// already-seeded keys are left alone.
require('dotenv').config();
const { Client, GatewayIntentBits, ChannelType } = require('discord.js');

const TOKEN = process.env.DISCORD_TOKEN;
const GUILD_ID = process.env.GUILD_ID;

if (!TOKEN || !GUILD_ID) {
  console.error('Missing DISCORD_TOKEN or GUILD_ID in .env');
  process.exit(1);
}

// { registryKey, channelName, marker, multi } — multi: true means match every
// embed's footer (rules/faq/staff-commands post several embeds in one message).
const TARGETS = [
  { key: 'rules', channel: 'rules', marker: 'stakeout-rules-v1', multi: true },
  { key: 'faq', channel: 'faq', marker: 'stakeout-faq-v1', multi: true },
  { key: 'role-panel', channel: 'choose-your-roles', marker: 'stakeout-role-panel-v1', multi: false },
  { key: 'ticket-panel', channel: 'open-a-ticket', marker: 'stakeout-ticket-panel-v1', multi: false },
  { key: 'music-help', channel: 'music-commands', marker: 'stakeout-music-help-v1', multi: false },
  { key: 'leaderboard-xp', channel: 'leaderboard', marker: 'stakeout-leaderboard-v1', multi: false },
  { key: 'leaderboard-coins', channel: 'leaderboard', marker: 'stakeout-leaderboard-coins-v1', multi: false },
  { key: 'staff-commands', channel: 'bot-commands', marker: 'stakeout-staff-commands-v1', multi: true },
];

const client = new Client({ intents: [GatewayIntentBits.Guilds, GatewayIntentBits.GuildMessages] });

client.once('clientReady', async () => {
  try {
    const guild = await client.guilds.fetch(GUILD_ID);
    await guild.channels.fetch();

    const registryPath = require('path').join(__dirname, '..', 'data', 'panel-messages.json');
    const fs = require('fs');
    let store = {};
    try {
      store = JSON.parse(fs.readFileSync(registryPath, 'utf8'));
    } catch {
      store = {};
    }

    for (const target of TARGETS) {
      if (store[target.key]) {
        console.log(`  already seeded: ${target.key}`);
        continue;
      }
      const channel = guild.channels.cache.find(
        (c) => c.type === ChannelType.GuildText && c.name === target.channel,
      );
      if (!channel) {
        console.warn(`  ! channel not found: #${target.channel}`);
        continue;
      }
      const messages = await channel.messages.fetch({ limit: 50 });
      const match = messages.find(
        (m) =>
          m.author.id === client.user.id &&
          (target.multi
            ? m.embeds.some((e) => e.footer?.text === target.marker)
            : m.embeds[0]?.footer?.text === target.marker),
      );
      if (match) {
        store[target.key] = match.id;
        console.log(`  seeded: ${target.key} -> ${match.id} (#${target.channel})`);
      } else {
        console.log(`  no old marker message found for ${target.key} in #${target.channel} — will post fresh`);
      }
    }

    fs.mkdirSync(require('path').dirname(registryPath), { recursive: true });
    fs.writeFileSync(registryPath, JSON.stringify(store, null, 2));

    console.log('\nDone.');
    process.exit(0);
  } catch (err) {
    console.error(err);
    process.exit(1);
  }
});

client.login(TOKEN);
