// One-off: configures Discord's native Guild Onboarding (the
// "Вопросы для персонализации" flow shown during Rules Screening,
// like on the S.T.A.L.K.E.R. G.A.M.M.A. server) so new members can
// pick their games and grant themselves roles before they even land
// on the server, on top of the #choose-your-roles panel.
require('dotenv').config();
const { Client, GatewayIntentBits, GuildOnboardingPromptType, GuildOnboardingMode } = require('discord.js');
const { GAMES, EXTRA_ROLES } = require('../config');

const TOKEN = process.env.DISCORD_TOKEN;
const GUILD_ID = process.env.GUILD_ID;

if (!TOKEN || !GUILD_ID) {
  console.error('Missing DISCORD_TOKEN or GUILD_ID in .env');
  process.exit(1);
}

const client = new Client({ intents: [GatewayIntentBits.Guilds] });

let idCounter = 0;
function fakeId() {
  // Discord requires an id per prompt/option even for brand-new ones —
  // it's discarded and replaced server-side, any unique string works.
  idCounter += 1;
  return `${Date.now()}${idCounter}`;
}

client.once('clientReady', async () => {
  try {
    const guild = await client.guilds.fetch(GUILD_ID);
    await guild.roles.fetch();
    await guild.channels.fetch();

    const gamesPrompt = {
      id: fakeId(),
      title: 'Какие игры тебе интересны?',
      singleSelect: false,
      required: false,
      inOnboarding: true,
      type: GuildOnboardingPromptType.Dropdown,
      options: GAMES.map((g) => {
        const role = guild.roles.cache.find((r) => r.name === g.name);
        return {
          id: fakeId(),
          title: g.name,
          emoji: g.emoji,
          roles: role ? [role.id] : [],
        };
      }),
    };

    const extraPrompt = {
      id: fakeId(),
      title: 'Что ещё интересно?',
      singleSelect: false,
      required: false,
      inOnboarding: true,
      type: GuildOnboardingPromptType.MultipleChoice,
      options: EXTRA_ROLES.map((r) => {
        const role = guild.roles.cache.find((role) => role.name === r.name);
        return {
          id: fakeId(),
          title: r.name,
          description: r.description,
          emoji: r.emoji,
          roles: role ? [role.id] : [],
        };
      }),
    };

    const defaultChannelNames = ['welcome', 'rules', 'announcements', 'choose-your-roles', 'faq', 'general', 'gaming-talk'];
    const defaultChannels = defaultChannelNames
      .map((name) => guild.channels.cache.find((c) => c.name === name))
      .filter(Boolean);

    await guild.editOnboarding({
      prompts: [gamesPrompt, extraPrompt],
      defaultChannels,
      enabled: true,
      mode: GuildOnboardingMode.OnboardingAdvanced,
    });

    console.log('Onboarding configured:');
    console.log('  prompts: Какие игры тебе интересны? (12 опций), Что ещё интересно? (2 опции)');
    console.log('  default channels:', defaultChannels.map((c) => c.name).join(', '));
    process.exit(0);
  } catch (err) {
    console.error(err);
    process.exit(1);
  }
});

client.login(TOKEN);
