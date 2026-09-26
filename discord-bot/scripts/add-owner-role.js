// One-off migration for the already-provisioned server: creates the
// Owner role (Administrator permission, same as Admin but positioned
// above it) and assigns it to the server owner. Safe to re-run.
require('dotenv').config();
const { Client, GatewayIntentBits, PermissionFlagsBits } = require('discord.js');
const { STAFF_ROLES } = require('../config');

const TOKEN = process.env.DISCORD_TOKEN;
const GUILD_ID = process.env.GUILD_ID;

if (!TOKEN || !GUILD_ID) {
  console.error('Missing DISCORD_TOKEN or GUILD_ID in .env');
  process.exit(1);
}

const ownerRoleDef = STAFF_ROLES.find((r) => r.key === 'owner');
const adminRoleDef = STAFF_ROLES.find((r) => r.key === 'admin');

const client = new Client({ intents: [GatewayIntentBits.Guilds] });

client.once('clientReady', async () => {
  try {
    const guild = await client.guilds.fetch(GUILD_ID);
    await guild.roles.fetch();

    console.log('== Owner role ==');
    let ownerRole = guild.roles.cache.find((r) => r.name === ownerRoleDef.name);
    if (!ownerRole) {
      ownerRole = await guild.roles.create({
        name: ownerRoleDef.name,
        color: ownerRoleDef.color,
        hoist: ownerRoleDef.hoist,
        permissions: ownerRoleDef.permissions,
      });
      console.log('  + role: Owner');
    } else {
      console.log('  already exists: Owner');
    }

    const adminRole = guild.roles.cache.find((r) => r.name === adminRoleDef.name);
    if (adminRole && ownerRole.position <= adminRole.position) {
      await ownerRole.setPosition(adminRole.position + 1);
      console.log('  repositioned Owner above Admin');
    }

    console.log('== assigning to server owner ==');
    const ownerMember = await guild.fetchOwner();
    if (ownerMember.roles.cache.has(ownerRole.id)) {
      console.log(`  already has the role: ${ownerMember.user.tag}`);
    } else {
      await ownerMember.roles.add(ownerRole);
      console.log(`  + assigned to ${ownerMember.user.tag}`);
    }

    console.log('\nDone.');
    process.exit(0);
  } catch (err) {
    console.error(err);
    process.exit(1);
  }
});

client.login(TOKEN);
