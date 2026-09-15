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
const { postFaq } = require('./features/faqPost');
const {
  handleMemberAdd: handleWelcomeAdd,
  handleMemberUpdate: handleWelcomeUpdate,
} = require('./features/welcome');
const { handleMessage: handleModCommand } = require('./features/modCommands');
const { handleMessage: handleInfoCommand } = require('./features/infoCommands');
const { startTicker: startTempbanTicker } = require('./features/tempbans');
const { lockStaffOnlyCategory, postCommandReference } = require('./features/staffDocs');
const { startLeaderboardTicker } = require('./features/leaderboard');
const { handleDelete: handleMessageLogDelete, handleEdit: handleMessageLogEdit } = require('./features/messageLog');
const {
  handleMemberUpdate: handleBoosterUpdate,
  handleColorCommand,
  ensureBoosterRole,
} = require('./features/boosters');
const { handleMessage: handleEconomyCommand } = require('./features/economy');
const { startStatTicker } = require('./features/statChannels');
const { handleMemberRemove: handleStickyRemove, handleMemberAdd: handleStickyAdd } = require('./features/stickyRoles');
const {
  registerCommand: registerReportCommand,
  handleContextMenu: handleReportContextMenu,
  handleModalSubmit: handleReportModalSubmit,
} = require('./features/quickReport');
const { handleReactionAdd: handleTranslateReaction } = require('./features/translate');
const { startFreeGamesTicker } = require('./features/freeGames');
const { postSteamInfo } = require('./features/steamInfo');

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
    GatewayIntentBits.GuildMessageReactions,
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
    await postFaq(guild);
    await lockStaffOnlyCategory(guild);
    await postCommandReference(guild);
    await ensureBoosterRole(guild);
    await registerReportCommand(guild);
    await postSteamInfo(guild);
  } catch (err) {
    console.error('Startup setup failed:', err);
  }
  startVoiceTicker(client);
  startTempbanTicker(client);
  startLeaderboardTicker(client, GUILD_ID);
  startStatTicker(client, GUILD_ID);
  startFreeGamesTicker(client, GUILD_ID);
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
  handleModCommand(message).catch((err) => {
    console.error('Mod command failed:', err);
  });
  handleInfoCommand(message).catch((err) => {
    console.error('Info command failed:', err);
  });
  handleColorCommand(message).catch((err) => {
    console.error('Color command failed:', err);
  });
  handleEconomyCommand(message).catch((err) => {
    console.error('Economy command failed:', err);
  });
});

client.on('guildMemberAdd', (member) => {
  handleMemberAdd(member).catch((err) => {
    console.error('Anti-raid check failed:', err);
  });
  handleWelcomeAdd(member).catch((err) => {
    console.error('Welcome greeting failed:', err);
  });
  handleStickyAdd(member).catch((err) => {
    console.error('Sticky role restore failed:', err);
  });
});

client.on('guildMemberRemove', (member) => {
  handleStickyRemove(member).catch((err) => {
    console.error('Sticky role save failed:', err);
  });
});

client.on('guildMemberUpdate', (oldMember, newMember) => {
  handleWelcomeUpdate(oldMember, newMember).catch((err) => {
    console.error('Welcome greeting (post-screening) failed:', err);
  });
  handleBoosterUpdate(oldMember, newMember).catch((err) => {
    console.error('Booster role assignment failed:', err);
  });
});

client.on('messageDelete', (message) => {
  handleMessageLogDelete(message).catch((err) => {
    console.error('Message-delete logging failed:', err);
  });
});

client.on('messageUpdate', (oldMessage, newMessage) => {
  handleMessageLogEdit(oldMessage, newMessage).catch((err) => {
    console.error('Message-edit logging failed:', err);
  });
});

client.on('messageReactionAdd', (reaction, user) => {
  handleTranslateReaction(reaction, user).catch((err) => {
    console.error('Translate reaction failed:', err);
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
    } else if (interaction.isMessageContextMenuCommand()) {
      await handleReportContextMenu(interaction);
    } else if (interaction.isModalSubmit() && interaction.customId.startsWith('report-modal-')) {
      await handleReportModalSubmit(interaction);
    }
  } catch (err) {
    console.error('Interaction failed:', err);
    if (interaction.deferred || interaction.replied) {
      await interaction.editReply('Что-то пошло не так, попробуй ещё раз.').catch(() => {});
    }
  }
});

client.login(TOKEN);
