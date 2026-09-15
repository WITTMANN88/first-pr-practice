// Shared config for provision.js and the running bot — one source of truth
// for role/category names so they never drift apart.
const { PermissionFlagsBits } = require('discord.js');

const STAFF_ROLES = [
  { name: 'Admin', color: 'DarkRed', hoist: true, permissions: [PermissionFlagsBits.Administrator] },
  { name: 'Moderator', color: 'Red', hoist: true, permissions: [PermissionFlagsBits.ModerateMembers, PermissionFlagsBits.ManageMessages, PermissionFlagsBits.KickMembers] },
  { name: 'Helper', color: 'Grey', hoist: true, permissions: [PermissionFlagsBits.ManageMessages] },
];

const RANK_LADDER = ['Recruit', 'Private', 'Corporal', 'Sergeant', 'Lieutenant', 'Captain', 'Major', 'Commander'];

const GAMES = [
  { key: 'operator', name: 'Operator', emoji: '🎯' },
  { key: 'ready-or-not', name: 'Ready or Not', emoji: '🚔' },
  { key: 'arma', name: 'Arma (3 + Reforger)', emoji: '🪖' },
  { key: 'tarkov', name: 'Escape from Tarkov', emoji: '🎒' },
  { key: 'ground-branch', name: 'Ground Branch', emoji: '🔫' },
  { key: 'squad', name: 'Squad', emoji: '🪓' },
  { key: 'insurgency', name: 'Insurgency: Sandstorm', emoji: '💣' },
  { key: 'bodycam', name: 'BodyCam', emoji: '📹' },
  { key: 'cod', name: 'Call of Duty', emoji: '🎮' },
  { key: 'grayzone', name: 'Gray Zone Warfare', emoji: '🌫️' },
  { key: 'battlefield', name: 'Battlefield', emoji: '⚔️' },
  { key: 'dayz', name: 'DayZ', emoji: '🧟' },
];

const EXTRA_ROLES = [
  { key: 'politics', name: 'Politics', emoji: '🗳️', description: 'Открывает SERIOUS TALK' },
  { key: 'other-games', name: 'Other Games', emoji: '🎲', description: 'Открывает категорию Other Games' },
];

const LFG_TAGS = ['EU', 'NA', 'Casual', 'Hardcore', 'Need players', 'Full squad'];

module.exports = { STAFF_ROLES, RANK_LADDER, GAMES, EXTRA_ROLES, LFG_TAGS };
