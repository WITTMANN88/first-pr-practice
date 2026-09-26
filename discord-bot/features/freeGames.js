// Posts new free-game giveaways (Steam/Epic/GOG/...) to #объявления.
// Uses GamerPower's free public API — no key needed. First run only
// seeds the "seen" list (no posts) so startup doesn't flood the
// channel with every giveaway currently live.
const fs = require('fs');
const path = require('path');
const { EmbedBuilder } = require('discord.js');
const COLORS = require('./colors');
const { CHANNELS } = require('../config');
const { ensureRussian } = require('./translate');

const DATA_PATH = path.join(__dirname, '..', 'data', 'free-games-seen.json');
const API_URL = 'https://www.gamerpower.com/api/giveaways?platform=pc';
const CHECK_MS = 30 * 60_000;
const TYPE_RU = { Game: 'Игра', DLC: 'Дополнение', 'Early Access': 'Ранний доступ', Other: 'Другое' };

let seen = new Set();
let isFirstRun = true;

function load() {
  try {
    seen = new Set(JSON.parse(fs.readFileSync(DATA_PATH, 'utf8')));
    isFirstRun = false;
  } catch {
    seen = new Set();
    isFirstRun = true;
  }
}

function save() {
  fs.mkdirSync(path.dirname(DATA_PATH), { recursive: true });
  fs.writeFileSync(DATA_PATH, JSON.stringify([...seen]));
}

async function checkFreeGames(guild) {
  const channel = guild.channels.cache.find((c) => c.name === CHANNELS.ANNOUNCEMENTS);
  if (!channel?.isTextBased()) return;

  let giveaways;
  try {
    const res = await fetch(API_URL, { headers: { 'User-Agent': 'stakeout-bot' } });
    giveaways = await res.json();
  } catch (err) {
    console.error('Free games fetch failed:', err.message);
    return;
  }
  if (!Array.isArray(giveaways)) return;

  if (isFirstRun) {
    giveaways.forEach((g) => seen.add(String(g.id)));
    save();
    isFirstRun = false;
    console.log(`Free games: seeded ${giveaways.length} existing giveaways (baseline, no posts)`);
    return;
  }

  const fresh = giveaways.filter((g) => !seen.has(String(g.id)));
  for (const g of fresh) {
    seen.add(String(g.id));
    const embed = new EmbedBuilder()
      .setTitle(`🎁 ${(g.title || '').replace(/\s*Giveaway$/i, '')}`)
      .setURL(g.open_giveaway_url || g.gamerpower_url)
      .setDescription((await ensureRussian(g.description || '')).slice(0, 300))
      .addFields(
        { name: 'Платформа', value: (g.platforms || 'неизвестно').replace('DRM-Free', 'без DRM'), inline: true },
        { name: 'Тип', value: TYPE_RU[g.type] || g.type || 'Игра', inline: true },
        { name: 'Обычная цена', value: g.worth && g.worth !== 'N/A' ? g.worth : 'нет данных', inline: true },
      )
      .setColor(COLORS.SUCCESS);
    if (g.image) embed.setImage(g.image);

    await channel.send({ embeds: [embed] }).catch(() => {});
  }
  if (fresh.length) save();
}

function startFreeGamesTicker(client, guildId) {
  const tick = async () => {
    try {
      const guild = await client.guilds.fetch(guildId);
      await checkFreeGames(guild);
    } catch (err) {
      console.error('Free games ticker failed:', err);
    }
  };
  tick();
  setInterval(tick, CHECK_MS);
}

load();

module.exports = { startFreeGamesTicker };
