// Remembers a member's roles when they leave and restores them on
// rejoin — except staff roles (Admin/Moderator/Helper), which never
// auto-restore. If someone was removed from staff for cause, rejoining
// must not hand that back automatically.
const fs = require('fs');
const path = require('path');
const { STAFF_ROLES } = require('../config');

const DATA_PATH = path.join(__dirname, '..', 'data', 'sticky-roles.json');
const STAFF_ROLE_NAMES = STAFF_ROLES.map((r) => r.name);

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

async function handleMemberRemove(member) {
  const roleIds = member.roles.cache
    .filter((r) => r.id !== member.guild.id && !STAFF_ROLE_NAMES.includes(r.name))
    .map((r) => r.id);
  if (roleIds.length === 0) return;
  store[key(member.guild.id, member.id)] = roleIds;
  save();
}

async function handleMemberAdd(member) {
  const k = key(member.guild.id, member.id);
  const roleIds = store[k];
  if (!roleIds?.length) return;

  const validRoleIds = roleIds.filter((id) => member.guild.roles.cache.has(id));
  if (validRoleIds.length) {
    await member.roles.add(validRoleIds).catch(() => {});
  }
  delete store[k];
  save();
}

load();

module.exports = { handleMemberRemove, handleMemberAdd };
