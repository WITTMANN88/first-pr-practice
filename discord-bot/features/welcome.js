// Public greeting in #добро-пожаловать. With Community mode + Membership
// Screening on, a joining member is "pending" until they accept the
// rules and can't see any channels yet — so we wait for the pending
// flag to clear (guildMemberUpdate) instead of greeting on raw join.
const { CHANNELS } = require('../config');

async function postGreeting(member) {
  const guild = member.guild;
  const channel = guild.channels.cache.find((c) => c.name === CHANNELS.WELCOME);
  if (!channel?.isTextBased()) return;

  const rules = guild.channels.cache.find((c) => c.name === CHANNELS.RULES);
  const rolesChannel = guild.channels.cache.find((c) => c.name === CHANNELS.ROLES);

  const parts = [`👋 <@${member.id}>, добро пожаловать в **STAKEOUT**!`];
  if (rules) parts.push(`Правила — в <#${rules.id}>.`);
  if (rolesChannel) parts.push(`Роли на игры и доступ к категориям — в <#${rolesChannel.id}>.`);

  await channel.send(parts.join(' ')).catch(() => {});
}

async function handleMemberAdd(member) {
  if (!member.pending) {
    await postGreeting(member);
  }
}

async function handleMemberUpdate(oldMember, newMember) {
  if (oldMember.pending && !newMember.pending) {
    await postGreeting(newMember);
  }
}

module.exports = { handleMemberAdd, handleMemberUpdate };
