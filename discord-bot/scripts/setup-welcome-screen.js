// One-off: configures the native Welcome Screen — what new members see
// right after accepting rules, before they land in a channel — with a
// short intro and the 5 channels worth visiting first. Safe to re-run.
require('dotenv').config();
const { Client, GatewayIntentBits, ChannelType } = require('discord.js');
const { CHANNELS } = require('../config');

const TOKEN = process.env.DISCORD_TOKEN;
const GUILD_ID = process.env.GUILD_ID;

if (!TOKEN || !GUILD_ID) {
  console.error('Missing DISCORD_TOKEN or GUILD_ID in .env');
  process.exit(1);
}

const WELCOME_CHANNELS = [
  { name: CHANNELS.RULES, emoji: '📜', description: 'Прочитай правила сервера' },
  { name: CHANNELS.ROLES, emoji: '🎮', description: 'Выбери роли — открой доступ к играм' },
  { name: CHANNELS.FAQ, emoji: '❓', description: 'Частые вопросы' },
  { name: CHANNELS.GENERAL, emoji: '💬', description: 'Общий чат — знакомься с сообществом' },
  { name: CHANNELS.TICKET, emoji: '🎫', description: 'Нужна помощь? Создай обращение' },
];

const client = new Client({ intents: [GatewayIntentBits.Guilds] });

client.once('clientReady', async () => {
  try {
    const guild = await client.guilds.fetch(GUILD_ID);
    await guild.channels.fetch();

    const welcomeChannels = WELCOME_CHANNELS.map(({ name, emoji, description }) => {
      const channel = guild.channels.cache.find((c) => c.type === ChannelType.GuildText && c.name === name);
      if (!channel) {
        console.warn(`  ! channel not found: #${name} — skipping`);
        return null;
      }
      return { channel, description, emoji };
    }).filter(Boolean);

    await guild.editWelcomeScreen({
      enabled: true,
      description: 'Добро пожаловать в STAKEOUT — тёмное тактическое коммьюнити. Начни с этого:',
      welcomeChannels,
    });

    console.log(`Welcome Screen configured with ${welcomeChannels.length} channel(s).`);
    console.log('\nDone.');
    process.exit(0);
  } catch (err) {
    console.error(err);
    process.exit(1);
  }
});

client.login(TOKEN);
