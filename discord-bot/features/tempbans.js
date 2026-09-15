// Persistent tempban store + ticker that unbans on schedule — survives
// bot restarts, unlike a plain setTimeout.
const fs = require('fs');
const path = require('path');

const DATA_PATH = path.join(__dirname, '..', 'data', 'tempbans.json');
const TICK_MS = 60_000;
let store = {};

function load() {
  try {
    store = JSON.parse(fs.readFileSync(DATA_PATH, 'utf8'));
  } catch {
    store = {};
  }
}

function save() {
  fs.mkdirSync(path.dirname(DATA_PATH), { recursive: true });
  fs.writeFileSync(DATA_PATH, JSON.stringify(store, null, 2));
}

function key(guildId, userId) {
  return `${guildId}:${userId}`;
}

function schedule(guildId, userId, unbanAt) {
  store[key(guildId, userId)] = { guildId, userId, unbanAt };
  save();
}

function cancel(guildId, userId) {
  delete store[key(guildId, userId)];
  save();
}

function startTicker(client) {
  load();
  setInterval(async () => {
    const now = Date.now();
    for (const [k, entry] of Object.entries(store)) {
      if (entry.unbanAt > now) continue;
      try {
        const guild = await client.guilds.fetch(entry.guildId);
        await guild.members.unban(entry.userId, 'Срок временного бана истёк');
        console.log(`Tempban expired, unbanned ${entry.userId} in ${entry.guildId}`);
      } catch (err) {
        console.warn(`Tempban auto-unban failed for ${entry.userId}:`, err.message);
      }
      delete store[k];
    }
    save();
  }, TICK_MS);
}

module.exports = { schedule, cancel, startTicker };
