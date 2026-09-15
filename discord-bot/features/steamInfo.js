// Posts a Steam info card into a dedicated "-info" channel in each
// game category — kept first in the category's channel order — plus a
// separate trailer teaser message. Steam's public API no longer
// exposes a direct playable video file (only DASH/HLS streaming
// manifests, which Discord can't embed inline), so the trailer message
// links out to Steam rather than claiming to play in-chat.
const { ChannelType, EmbedBuilder, PermissionFlagsBits } = require('discord.js');
const { GAMES } = require('../config');
const { upsertPanel } = require('./messageRegistry');

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

function buildInfoEmbeds(data, url) {
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

function buildTrailerEmbed(data, url) {
  const movie = data.movies?.[0];
  if (!movie) return null;
  return new EmbedBuilder()
    .setTitle(`🎬 Трейлер — ${movie.name || data.name}`)
    .setURL(url)
    .setDescription('Steam больше не отдаёт трейлеры как обычный видеофайл (только потоковый формат, Discord его не проигрывает встроенно) — жми, чтобы посмотреть на странице игры.')
    .setImage(movie.thumbnail)
    .setColor(0x8b0000);
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

  await moveToTopOfCategory(guild, category, channel);
  return channel;
}

async function moveToTopOfCategory(guild, category, channel) {
  const siblings = [...category.children.cache.values()].sort((a, b) => a.position - b.position);
  if (siblings[0]?.id === channel.id) return; // already first, nothing to do

  const rest = siblings.filter((c) => c.id !== channel.id);
  const newOrder = [channel, ...rest];
  await guild.channels.setPositions(newOrder.map((c, i) => ({ channel: c.id, position: i })));
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
      const data = await fetchAppDetails(appid).catch((err) => {
        console.warn(`  ! Steam fetch failed for appid ${appid}: ${err.message}`);
        return null;
      });
      if (!data) continue;

      const url = `https://store.steampowered.com/app/${data.steam_appid}/`;

      await upsertPanel(channel, `steam-info-${appid}`, { embeds: buildInfoEmbeds(data, url) });
      console.log(`  synced Steam info: ${data.name} -> #${channel.name}`);

      const trailerEmbed = buildTrailerEmbed(data, url);
      if (trailerEmbed) {
        await upsertPanel(channel, `steam-trailer-${appid}`, { embeds: [trailerEmbed] });
        console.log(`  synced trailer: ${data.name} -> #${channel.name}`);
      }
    }
  }
}

module.exports = { postSteamInfo };
