// One-off setup: creates STAKEOUT's Discord AutoMod rules (native
// feature, no bot needed to run it — this script just configures it
// once via the API instead of clicking through Server Settings).
// Safe to re-run: skips rules that already exist by name.
require('dotenv').config();
const {
  Client,
  GatewayIntentBits,
  AutoModerationRuleEventType,
  AutoModerationRuleTriggerType,
  AutoModerationRuleKeywordPresetType,
  AutoModerationActionType,
} = require('discord.js');
const { STAFF_ROLES, CHANNELS } = require('../config');

const TOKEN = process.env.DISCORD_TOKEN;
const GUILD_ID = process.env.GUILD_ID;

if (!TOKEN || !GUILD_ID) {
  console.error('Missing DISCORD_TOKEN or GUILD_ID in .env');
  process.exit(1);
}

const RULE_NAMES = {
  SLURS: 'STAKEOUT — Оскорбления и ненависть',
  MENTIONS: 'STAKEOUT — Спам упоминаниями',
  INVITES: 'STAKEOUT — Ссылки-приглашения',
};

const client = new Client({ intents: [GatewayIntentBits.Guilds] });

async function findOrCreateRule(guild, name, options) {
  const existing = (await guild.autoModerationRules.fetch()).find((r) => r.name === name);
  if (existing) {
    console.log(`  = already exists: ${name}`);
    return existing;
  }
  const rule = await guild.autoModerationRules.create({ name, ...options });
  console.log(`  + created: ${name}`);
  return rule;
}

client.once('clientReady', async () => {
  try {
    const guild = await client.guilds.fetch(GUILD_ID);
    const modLogs = guild.channels.cache.find((c) => c.name === CHANNELS.MOD_LOGS);
    const staffRoleIds = STAFF_ROLES.map((r) => guild.roles.cache.find((role) => role.name === r.name)?.id).filter(
      Boolean,
    );
    const selfPromo = guild.channels.cache.find((c) => c.name === CHANNELS.SELF_PROMO);

    await findOrCreateRule(guild, RULE_NAMES.SLURS, {
      eventType: AutoModerationRuleEventType.MessageSend,
      triggerType: AutoModerationRuleTriggerType.KeywordPreset,
      triggerMetadata: { presets: [AutoModerationRuleKeywordPresetType.Slurs] },
      actions: [
        { type: AutoModerationActionType.BlockMessage },
        ...(modLogs ? [{ type: AutoModerationActionType.SendAlertMessage, metadata: { channel: modLogs.id } }] : []),
      ],
      exemptRoles: staffRoleIds,
      enabled: true,
    });

    await findOrCreateRule(guild, RULE_NAMES.MENTIONS, {
      eventType: AutoModerationRuleEventType.MessageSend,
      triggerType: AutoModerationRuleTriggerType.MentionSpam,
      triggerMetadata: { mentionTotalLimit: 5 },
      actions: [{ type: AutoModerationActionType.BlockMessage }],
      exemptRoles: staffRoleIds,
      enabled: true,
    });

    await findOrCreateRule(guild, RULE_NAMES.INVITES, {
      eventType: AutoModerationRuleEventType.MessageSend,
      triggerType: AutoModerationRuleTriggerType.Keyword,
      triggerMetadata: {
        keywordFilter: ['*discord.gg*', '*discord.com/invite*', '*discordapp.com/invite*'],
      },
      actions: [{ type: AutoModerationActionType.BlockMessage }],
      exemptRoles: staffRoleIds,
      exemptChannels: selfPromo ? [selfPromo.id] : [],
      enabled: true,
    });

    console.log('\nAutoMod rules set up.');
    process.exit(0);
  } catch (err) {
    console.error(err);
    process.exit(1);
  }
});

client.login(TOKEN);
