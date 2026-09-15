// One-off migration for the already-provisioned server: renames
// SUPPORT and SERIOUS TALK to their bilingual names in place (same
// category, same channels/permissions inside — just a new label),
// and adds the Content Creator role + #content-creators channel.
// Safe to re-run.
require('dotenv').config();
const { Client, GatewayIntentBits, ChannelType, PermissionFlagsBits } = require('discord.js');
const { STAFF_ROLES, CONTENT_CREATOR_ROLE } = require('../config');

const TOKEN = process.env.DISCORD_TOKEN;
const GUILD_ID = process.env.GUILD_ID;

if (!TOKEN || !GUILD_ID) {
  console.error('Missing DISCORD_TOKEN or GUILD_ID in .env');
  process.exit(1);
}

const RENAMES = [
  { from: '🎫 SUPPORT', to: '🎫 SUPPORT / ПОДДЕРЖКА' },
  { from: '🌍 SERIOUS TALK', to: '🌍 SERIOUS TALK / СЕРЬЁЗНЫЙ РАЗГОВОР' },
];

const client = new Client({ intents: [GatewayIntentBits.Guilds] });

client.once('clientReady', async () => {
  try {
    const guild = await client.guilds.fetch(GUILD_ID);
    await guild.channels.fetch();
    await guild.roles.fetch();

    console.log('== renaming categories ==');
    for (const { from, to } of RENAMES) {
      const category = guild.channels.cache.find((c) => c.type === ChannelType.GuildCategory && c.name === from);
      if (category) {
        await category.setName(to);
        console.log(`  renamed: ${from} -> ${to}`);
      } else {
        const already = guild.channels.cache.find((c) => c.type === ChannelType.GuildCategory && c.name === to);
        console.log(already ? `  already renamed: ${to}` : `  ! category not found: ${from}`);
      }
    }

    console.log('== Content Creator role ==');
    let creatorRole = guild.roles.cache.find((r) => r.name === CONTENT_CREATOR_ROLE.name);
    if (!creatorRole) {
      creatorRole = await guild.roles.create({
        name: CONTENT_CREATOR_ROLE.name,
        color: CONTENT_CREATOR_ROLE.color,
        hoist: true,
        mentionable: false,
      });
      console.log(`  + role: ${CONTENT_CREATOR_ROLE.name}`);
    } else {
      console.log(`  already exists: ${CONTENT_CREATOR_ROLE.name}`);
    }

    console.log('== #content-creators channel ==');
    const community = guild.channels.cache.find(
      (c) => c.type === ChannelType.GuildCategory && c.name === '💬 COMMUNITY',
    );
    if (!community) {
      console.warn('  ! COMMUNITY category not found — skipping channel');
    } else {
      let channel = guild.channels.cache.find((c) => c.name === 'content-creators' && c.parentId === community.id);
      if (!channel) {
        const staffRoleIds = STAFF_ROLES.map((r) => guild.roles.cache.find((role) => role.name === r.name)?.id).filter(
          Boolean,
        );
        channel = await guild.channels.create({
          name: 'content-creators',
          type: ChannelType.GuildText,
          parent: community.id,
          permissionOverwrites: [
            { id: guild.roles.everyone.id, deny: [PermissionFlagsBits.SendMessages] },
            { id: creatorRole.id, allow: [PermissionFlagsBits.SendMessages] },
            ...staffRoleIds.map((id) => ({ id, allow: [PermissionFlagsBits.SendMessages] })),
          ],
        });
        console.log('  + channel: content-creators');
      } else {
        console.log('  already exists: content-creators');
      }
    }

    console.log('\nDone.');
    process.exit(0);
  } catch (err) {
    console.error(err);
    process.exit(1);
  }
});

client.login(TOKEN);
