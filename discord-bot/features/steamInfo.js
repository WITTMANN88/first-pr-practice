// Posts a Steam info card into a dedicated #инфо channel in each
// game category — kept first in the category's channel order — plus a
// separate trailer message. Steam's own trailer files aren't playable
// inline in Discord (streaming-only manifests), but a plain YouTube
// link IS auto-embedded by Discord as a playable inline video, so the
// trailer message posts a hand-picked official YouTube trailer link
// per game instead of a Steam thumbnail-and-click-through.
const { ChannelType, EmbedBuilder, PermissionFlagsBits } = require('discord.js');
const { GAMES, CHANNELS } = require('../config');
const { upsertPanel } = require('./messageRegistry');
const { ensureRussian } = require('./translate');
const COLORS = require('./colors');

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
  const res = await fetch(`https://store.steampowered.com/api/appdetails?appids=${appid}&cc=us&l=russian`, {
    headers: { 'User-Agent': 'stakeout-bot' },
  });
  const json = await res.json();
  // Steam now keys the response by an internal id rather than the
  // requested appid, so match on the steam_appid inside instead.
  const entry = Object.values(json).find((e) => e?.data?.steam_appid === appid);
  if (!entry?.success) return null;
  return entry.data;
}

function buildInfoEmbeds(data, url, description) {
  const main = new EmbedBuilder()
    .setTitle(data.name)
    .setURL(url)
    .setDescription((description || '').slice(0, 500))
    .setColor(COLORS.BRAND)
    .setImage(data.header_image)
    .addFields(
      { name: 'Steam', value: `[Открыть страницу](${url})`, inline: true },
      { name: 'Цена', value: data.is_free ? 'Бесплатно' : data.price_overview?.final_formatted || 'см. Steam', inline: true },
    );

  const gallery = (data.screenshots || []).slice(0, SCREENSHOT_COUNT).map((s) =>
    new EmbedBuilder().setURL(url).setImage(s.path_full).setColor(COLORS.BRAND),
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
    .setColor(COLORS.BRAND);
}

async function findOrCreateInfoChannel(guild, gameEmoji, gameName) {
  const category = guild.channels.cache.find(
    (c) => c.type === ChannelType.GuildCategory && c.name === `${gameEmoji} ${gameName}`,
  );
  if (!category) return null;

  let channel = guild.channels.cache.find((c) => c.name === CHANNELS.GAME_INFO && c.parentId === category.id);
  if (!channel) {
    channel = await guild.channels.create({
      name: CHANNELS.GAME_INFO,
      type: ChannelType.GuildText,
      parent: category.id,
      permissionOverwrites: [{ id: guild.roles.everyone.id, deny: [PermissionFlagsBits.SendMessages] }],
    });
    console.log(`  + info channel in ${gameName}`);
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

    const channel = await findOrCreateInfoChannel(guild, game.emoji, game.name);
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

      const description = await ensureRussian(data.short_description);
      await upsertPanel(channel, `steam-info-${appid}`, { embeds: buildInfoEmbeds(data, url, description) });
      console.log(`  synced Steam info: ${data.name}`);

      const youtubeId = YOUTUBE_TRAILERS[appid];
      if (youtubeId) {
        await upsertPanel(channel, `steam-trailer-${appid}`, {
          content: `🎬 Смотри трейлер ниже\nhttps://www.youtube.com/watch?v=${youtubeId}`,
          embeds: [],
        });
        console.log(`  synced trailer (YouTube): ${data.name}`);
      } else {
        const trailerEmbed = buildTrailerEmbed(data, url);
        if (trailerEmbed) {
          await upsertPanel(channel, `steam-trailer-${appid}`, { content: '', embeds: [trailerEmbed] });
          console.log(`  synced trailer (Steam): ${data.name}`);
        }
      }
    }
  }
}

module.exports = { postSteamInfo };
