// Posts a Steam info card into a dedicated "-info" channel in each
// game category: store link, description, and a small screenshot
// gallery (several embeds sharing one URL — Discord groups them into
// a tiled gallery instead of stacking separate blocks). Steam's
// appdetails API is public, no key needed.
const { ChannelType, EmbedBuilder, PermissionFlagsBits } = require('discord.js');
const { GAMES } = require('../config');

const MARKER_PREFIX = 'stakeout-steam-info-';
const SCREENSHOT_COUNT = 4;

// Some categories cover more than one Steam listing (Arma = 3 + Reforger).
const STEAM_APPIDS = {
  operator: [1913370],
  'ready-or-not': [1144200],
  arma: [107410, 1874880], // Arma 3, Arma Reforger
  tarkov: [3932890],
  'ground-branch': [16900],
  squad: [393380],
  insurgency: [581320],
  bodycam: [2406770],
  cod: [1938090],
  grayzone: [2479810],
  battlefield: [2807960],
  dayz: [221100],
};

async function fetchAppDetails(appid) {
  const res = await fetch(`https://store.steampowered.com/api/appdetails?appids=${appid}&cc=us&l=en`, {
    headers: { 'User-Agent': 'stakeout-bot' },
  });
  const json = await res.json();
  const entry = json[String(appid)];
  if (!entry?.success) return null;
  return entry.data;
}

function buildEmbeds(data) {
  const url = `https://store.steampowered.com/app/${data.steam_appid}/`;
  const main = new EmbedBuilder()
    .setTitle(data.name)
    .setURL(url)
    .setDescription((data.short_description || '').slice(0, 500))
    .setColor(0x8b0000)
    .setImage(data.header_image)
    .addFields(
      { name: 'Steam', value: `[Открыть страницу](${url})`, inline: true },
      { name: 'Цена', value: data.is_free ? 'Бесплатно' : data.price_overview?.final_formatted || 'см. Steam', inline: true },
    );

  const gallery = (data.screenshots || []).slice(0, SCREENSHOT_COUNT).map((s) =>
    new EmbedBuilder().setURL(url).setImage(s.path_full).setColor(0x8b0000),
  );

  return [main, ...gallery];
}

async function findOrCreateInfoChannel(guild, gameKey, gameEmoji, gameName) {
  const category = guild.channels.cache.find(
    (c) => c.type === ChannelType.GuildCategory && c.name === `${gameEmoji} ${gameName}`,
  );
  if (!category) return null;

  let channel = guild.channels.cache.find((c) => c.name === `${gameKey}-info` && c.parentId === category.id);
  if (!channel) {
    channel = await guild.channels.create({
      name: `${gameKey}-info`,
      type: ChannelType.GuildText,
      parent: category.id,
      permissionOverwrites: [{ id: guild.roles.everyone.id, deny: [PermissionFlagsBits.SendMessages] }],
    });
    console.log(`  + channel: ${gameKey}-info`);
  }
  return channel;
}

async function postSteamInfo(guild) {
  for (const game of GAMES) {
    const appids = STEAM_APPIDS[game.key];
    if (!appids) continue;

    const channel = await findOrCreateInfoChannel(guild, game.key, game.emoji, game.name);
    if (!channel) {
      console.warn(`  ! category not found for ${game.name} — skipping Steam info`);
      continue;
    }

    for (const appid of appids) {
      const marker = `${MARKER_PREFIX}${appid}`;
      const recent = await channel.messages.fetch({ limit: 20 });
      const existing = recent.find(
        (m) => m.author.id === guild.client.user.id && m.embeds.some((e) => e.footer?.text === marker),
      );
      if (existing) continue;

      const data = await fetchAppDetails(appid).catch((err) => {
        console.warn(`  ! Steam fetch failed for appid ${appid}: ${err.message}`);
        return null;
      });
      if (!data) continue;

      const embeds = buildEmbeds(data);
      embeds[embeds.length - 1].setFooter({ text: marker });
      await channel.send({ embeds }).catch((err) => console.warn(`  ! failed to post ${data.name}: ${err.message}`));
      console.log(`  posted Steam info: ${data.name} -> #${channel.name}`);
    }
  }
}

module.exports = { postSteamInfo };
