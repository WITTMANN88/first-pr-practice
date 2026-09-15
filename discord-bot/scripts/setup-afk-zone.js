// One-off: moves the AFK voice channel out of COMMUNITY into its own
// category at the very bottom of the channel list, and wires up
// Discord's native AFK timeout (auto-moves idle voice users there).
require('dotenv').config();
const { Client, GatewayIntentBits, ChannelType } = require('discord.js');

const TOKEN = process.env.DISCORD_TOKEN;
const GUILD_ID = process.env.GUILD_ID;
const AFK_CATEGORY_NAME = '💤 AFK ZONE';
const AFK_CHANNEL_NAME = '💤 AFK';
const AFK_TIMEOUT_SECONDS = 300; // 5 min — one of Discord's fixed allowed values

if (!TOKEN || !GUILD_ID) {
  console.error('Missing DISCORD_TOKEN or GUILD_ID in .env');
  process.exit(1);
}

const client = new Client({ intents: [GatewayIntentBits.Guilds] });

client.once('clientReady', async () => {
  try {
    const guild = await client.guilds.fetch(GUILD_ID);
    await guild.channels.fetch();

    let category = guild.channels.cache.find(
      (c) => c.type === ChannelType.GuildCategory && c.name === AFK_CATEGORY_NAME,
    );
    if (!category) {
      category = await guild.channels.create({ name: AFK_CATEGORY_NAME, type: ChannelType.GuildCategory });
      console.log(`+ category: ${AFK_CATEGORY_NAME}`);
    }

    let afkChannel = guild.channels.cache.find(
      (c) => c.type === ChannelType.GuildVoice && c.name === AFK_CHANNEL_NAME,
    );
    if (afkChannel) {
      await afkChannel.setParent(category.id, { lockPermissions: false });
      console.log(`  moved existing "${AFK_CHANNEL_NAME}" into ${AFK_CATEGORY_NAME}`);
    } else {
      afkChannel = await guild.channels.create({
        name: AFK_CHANNEL_NAME,
        type: ChannelType.GuildVoice,
        parent: category.id,
      });
      console.log(`  + channel: ${AFK_CHANNEL_NAME}`);
    }

    // Push the AFK category to the very bottom of the list.
    const categories = [...guild.channels.cache.filter((c) => c.type === ChannelType.GuildCategory).values()].sort(
      (a, b) => a.position - b.position,
    );
    const rest = categories.filter((c) => c.id !== category.id);
    const newOrder = [...rest, category];
    await guild.channels.setPositions(newOrder.map((c, i) => ({ channel: c.id, position: i })));
    console.log(`  positioned ${AFK_CATEGORY_NAME} at the bottom`);

    await guild.setAFKChannel(afkChannel);
    await guild.setAFKTimeout(AFK_TIMEOUT_SECONDS);
    console.log(`  set as server AFK channel, timeout ${AFK_TIMEOUT_SECONDS}s`);

    console.log('\nDone.');
    process.exit(0);
  } catch (err) {
    console.error(err);
    process.exit(1);
  }
});

client.login(TOKEN);
