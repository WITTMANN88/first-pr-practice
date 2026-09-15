// STAKEOUT — the persistent bot. Unlike provision.js this stays running:
// it keeps the role panel in sync and will grow more features over time
// (tickets, XP, join-to-create, ...) as separate modules under ./features.
require('dotenv').config();
const { Client, GatewayIntentBits } = require('discord.js');
const { registerRolePanel, handleRoleSelect } = require('./features/rolePanel');
const {
  postTicketPanel,
  handleTicketButton,
  handleTicketModalSubmit,
  handleTicketClose,
} = require('./features/tickets');
const { handleMessage: handleXpMessage, startVoiceTicker } = require('./features/xp');
const { handleVoiceStateUpdate } = require('./features/joinToCreate');
const { handleMemberAdd } = require('./features/antiRaid');
const { postMusicHelp, handleMessage: handleMusicMessage } = require('./features/music');
const { postRules } = require('./features/rulesPost');
const {
  handleMemberAdd: handleWelcomeAdd,
  handleMemberUpdate: handleWelcomeUpdate,
} = require('./features/welcome');

const TOKEN = process.env.DISCORD_TOKEN;
const GUILD_ID = process.env.GUILD_ID;

if (!TOKEN || !GUILD_ID) {
  console.error('Missing DISCORD_TOKEN or GUILD_ID — copy .env.example to .env and fill both in.');
  process.exit(1);
}

const client = new Client({
  intents: [
    GatewayIntentBits.Guilds,
    GatewayIntentBits.GuildMembers,
    GatewayIntentBits.GuildMessages,
    GatewayIntentBits.MessageContent,
    GatewayIntentBits.GuildVoiceStates,
  ],
});

client.once('clientReady', async () => {
  console.log(`Logged in as ${client.user.tag}`);
  try {
    const guild = await client.guilds.fetch(GUILD_ID);
    await registerRolePanel(guild);
    await postTicketPanel(guild);
    await postMusicHelp(guild);
    await postRules(guild);
  } catch (err) {
    console.error('Startup setup failed:', err);
  }
  startVoiceTicker(client);
  console.log('Bot is running. Leave this window open — closing it takes the role menu offline.');
});

client.on('messageCreate', (message) => {
  try {
    handleXpMessage(message);
  } catch (err) {
    console.error('XP message handling failed:', err);
  }
  handleMusicMessage(message).catch((err) => {
    console.error('Music command failed:', err);
  });
});

client.on('guildMemberAdd', (member) => {
  handleMemberAdd(member).catch((err) => {
    console.error('Anti-raid check failed:', err);
  });
  handleWelcomeAdd(member).catch((err) => {
    console.error('Welcome greeting failed:', err);
  });
});

client.on('guildMemberUpdate', (oldMember, newMember) => {
  handleWelcomeUpdate(oldMember, newMember).catch((err) => {
    console.error('Welcome greeting (post-screening) failed:', err);
  });
});

client.on('voiceStateUpdate', (oldState, newState) => {
  handleVoiceStateUpdate(oldState, newState).catch((err) => {
    console.error('Join-to-Create failed:', err);
  });
});

client.on('interactionCreate', async (interaction) => {
  try {
    if (interaction.isStringSelectMenu()) {
      await handleRoleSelect(interaction);
    } else if (interaction.isButton() && interaction.customId.startsWith('ticket-open-')) {
      await handleTicketButton(interaction);
    } else if (interaction.isButton() && interaction.customId === 'ticket-close') {
      await handleTicketClose(interaction);
    } else if (interaction.isModalSubmit() && interaction.customId.startsWith('ticket-modal-')) {
      await handleTicketModalSubmit(interaction);
    }
  } catch (err) {
    console.error('Interaction failed:', err);
    if (interaction.deferred || interaction.replied) {
      await interaction.editReply('Что-то пошло не так, попробуй ещё раз.').catch(() => {});
    }
  }
});

client.login(TOKEN);
