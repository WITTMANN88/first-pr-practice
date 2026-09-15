// One-off migration: renames the already-created rank roles in place
// (same role ID, same members, same color — just a new label) and
// updates the "rank" field already stored in data/xp.json so it keeps
// matching config.js's current RANK_LADDER. Safe to re-run.
require('dotenv').config();
const fs = require('fs');
const path = require('path');
const { Client, GatewayIntentBits } = require('discord.js');

const OLD_LADDER = ['Recruit', 'Private', 'Corporal', 'Sergeant', 'Lieutenant', 'Captain', 'Major', 'Commander'];
const NEW_LADDER = ['Newbie', 'Stalker', 'Veteran', 'Marksman', 'Trapper', 'Bandit Killer', 'Zone Master', 'Legend of the Zone'];

const TOKEN = process.env.DISCORD_TOKEN;
const GUILD_ID = process.env.GUILD_ID;

if (!TOKEN || !GUILD_ID) {
  console.error('Missing DISCORD_TOKEN or GUILD_ID in .env');
  process.exit(1);
}

const client = new Client({ intents: [GatewayIntentBits.Guilds] });

client.once('clientReady', async () => {
  try {
    const guild = await client.guilds.fetch(GUILD_ID);
    await guild.roles.fetch();

    for (let i = 0; i < OLD_LADDER.length; i++) {
      const role = guild.roles.cache.find((r) => r.name === OLD_LADDER[i]);
      if (role) {
        await role.setName(NEW_LADDER[i]);
        console.log(`  renamed role: ${OLD_LADDER[i]} -> ${NEW_LADDER[i]}`);
      } else {
        const already = guild.roles.cache.find((r) => r.name === NEW_LADDER[i]);
        console.log(already ? `  already renamed: ${NEW_LADDER[i]}` : `  ! role not found: ${OLD_LADDER[i]}`);
      }
    }

    const xpPath = path.join(__dirname, '..', 'data', 'xp.json');
    try {
      const data = JSON.parse(fs.readFileSync(xpPath, 'utf8'));
      let changed = 0;
      for (const entry of Object.values(data)) {
        const idx = OLD_LADDER.indexOf(entry.rank);
        if (idx !== -1) {
          entry.rank = NEW_LADDER[idx];
          changed++;
        }
      }
      fs.writeFileSync(xpPath, JSON.stringify(data, null, 2));
      console.log(`  updated ${changed} entr${changed === 1 ? 'y' : 'ies'} in data/xp.json`);
    } catch (err) {
      console.log(`  no xp.json to migrate yet: ${err.message}`);
    }

    console.log('\nDone.');
    process.exit(0);
  } catch (err) {
    console.error(err);
    process.exit(1);
  }
});

client.login(TOKEN);
