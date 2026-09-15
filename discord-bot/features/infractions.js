// Persistent warn history per user, keyed by guild+user.
const fs = require('fs');
const path = require('path');

const DATA_PATH = path.join(__dirname, '..', 'data', 'infractions.json');
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

function addWarn(guildId, userId, { reason, moderatorTag }) {
  const k = key(guildId, userId);
  const list = store[k] ?? [];
  list.push({ reason, moderatorTag, at: Date.now() });
  store[k] = list;
  save();
  return list.length;
}

function getInfractions(guildId, userId) {
  return store[key(guildId, userId)] ?? [];
}

function clearInfractions(guildId, userId) {
  delete store[key(guildId, userId)];
  save();
}

load();

module.exports = { addWarn, getInfractions, clearInfractions };
