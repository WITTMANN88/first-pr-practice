// Booster perk role — auto-granted the moment someone boosts, with a
// thank-you announcement in #welcome. What the role actually unlocks
// is still an open design question (see README) — for now it's just
// a colored badge.
const ROLE_NAME = 'STAKEOUT FRIEND';

async function ensureBoosterRole(guild) {
  let role = guild.roles.cache.find((r) => r.name === ROLE_NAME);
  if (!role) {
    role = await guild.roles.create({ name: ROLE_NAME, color: '#f47fff', hoist: true, mentionable: false });
    console.log(`+ role: ${ROLE_NAME}`);
  }
  return role;
}

async function handleMemberUpdate(oldMember, newMember) {
  const startedBoosting = !oldMember.premiumSinceTimestamp && newMember.premiumSinceTimestamp;
  if (!startedBoosting) return;

  const role = await ensureBoosterRole(newMember.guild);
  await newMember.roles.add(role).catch(() => {});

  const welcome = newMember.guild.channels.cache.find((c) => c.name === 'welcome');
  if (welcome?.isTextBased()) {
    await welcome
      .send(`🚀 <@${newMember.id}> забустил сервер — спасибо! Получена роль **${ROLE_NAME}**.`)
      .catch(() => {});
  }
}

module.exports = { handleMemberUpdate, ensureBoosterRole };
