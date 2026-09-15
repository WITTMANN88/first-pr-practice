// Tracks "this bot's one standing message per key" by message ID
// instead of a visible marker string in the embed footer — nothing
// technical shows up to users. Used by every panel that posts once
// and keeps itself updated in place (rules, FAQ, role panel, etc).
const fs = require('fs');
const path = require('path');

const DATA_PATH = path.join(__dirname, '..', 'data', 'panel-messages.json');
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

// Posts `payload` under `key`, or edits the previously posted message
// for that key if it still exists. Returns { message, isNew }.
async function upsertPanel(channel, key, payload) {
  const messageId = store[key];
  if (messageId) {
    try {
      const message = await channel.messages.fetch(messageId);
      await message.edit(payload);
      return { message, isNew: false };
    } catch {
      // message was deleted (manually, or channel recreated) — fall through and repost
    }
  }
  const message = await channel.send(payload);
  store[key] = message.id;
  save();
  return { message, isNew: true };
}

// True if `key` already has a live posted message — lets callers skip
// expensive work (e.g. a Steam API fetch) when there's nothing to do.
async function panelExists(channel, key) {
  const messageId = store[key];
  if (!messageId) return false;
  try {
    await channel.messages.fetch(messageId);
    return true;
  } catch {
    return false;
  }
}

load();

module.exports = { upsertPanel, panelExists };
