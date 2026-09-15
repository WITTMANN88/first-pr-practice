// Posts a Steam info card into a dedicated "-info" channel in each
// game category — kept first in the category's channel order — plus a
// separate trailer message. Steam's own trailer files aren't playable
// inline in Discord (streaming-only manifests), but a plain YouTube
// link IS auto-embedded by Discord as a playable inline video, so the
// trailer message posts a hand-picked official YouTube trailer link
// per game instead of a Steam thumbnail-and-click-through.
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

// Curated official trailers, keyed by Steam appid — picked by hand so
// the right video shows up (an automated search can't reliably tell an
// official trailer apart from fan content or a same-named game).
const YOUTUBE_TRAILERS = {
  1913370: 'f_hfe80mVXo', // OPERATOR — Early Access Gameplay Trailer
  1144200: '0PH_f3zo5_A', // Ready or Not — Official Gameplay Trailer
  107410: 'M1YBZUxMX8g', // Arma 3 — Launch Trailer
  1874880: 'mO499F5sUqc', // Arma Reforger — Official 1.0 Launch Trailer
  3932890: 'tFw0a3Ob4ME', // Escape from Tarkov — Official Gameplay 1.0 Launch Trailer
  16900: 'XDvSbktCyko', // Ground Branch — Official 1.0 Launch Trailer
  393380: 'UDnUD73gRXk', // Squad — Launch Trailer
  581320: 'tXc2M0ZHhYA', // Insurgency: Sandstorm — Launch Trailer
  2406770: 'OJtv52GuSWM', // Bodycam — Official Launch Trailer
  1938090: 'DU_3bKwO0nI', // Call of Duty: Black Ops 7 — Official Gameplay Reveal Trailer
  2479810: 'SOvNIeOtoqA', // Gray Zone Warfare — Official Early Access Launch Trailer
  2807960: 'pgNCgJG0vnY', // Battlefield 6 — Official Reveal Trailer
  221100: 'hUH2rrHtnFs', // DayZ — Every Day Is a New Story (Cinematic Trailer)
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

      const youtubeId = YOUTUBE_TRAILERS[appid];
      if (youtubeId) {
        await upsertPanel(channel, `steam-trailer-${appid}`, {
          content: `https://www.youtube.com/watch?v=${youtubeId}`,
          embeds: [],
        });
        console.log(`  synced trailer (YouTube): ${data.name} -> #${channel.name}`);
      } else {
        const trailerEmbed = buildTrailerEmbed(data, url);
        if (trailerEmbed) {
          await upsertPanel(channel, `steam-trailer-${appid}`, { content: '', embeds: [trailerEmbed] });
          console.log(`  synced trailer (Steam): ${data.name} -> #${channel.name}`);
        }
      }
    }
  }
}

module.exports = { postSteamInfo };
