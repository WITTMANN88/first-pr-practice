// Shared config for provision.js and the running bot — one source of truth
// for every role/category/channel name the code looks things up by, so
// the code and the live server never drift apart.
const { PermissionFlagsBits } = require('discord.js');

// Owner is created first so it lands above Admin in the role hierarchy
// on a fresh provision (each new role is inserted just above
// @everyone, pushing earlier-created roles up — so create order here
// is highest-to-lowest). On the already-live server, the migration
// script repositions it explicitly instead.
const STAFF_ROLES = [
  { key: 'owner', name: 'Владелец', color: '#FFFFFF', hoist: true, canPunish: true, permissions: [PermissionFlagsBits.Administrator] },
  { key: 'admin', name: 'Администратор', color: 'DarkRed', hoist: true, canPunish: true, permissions: [PermissionFlagsBits.Administrator] },
  { key: 'moderator', name: 'Модератор', color: 'Red', hoist: true, canPunish: true, permissions: [PermissionFlagsBits.ModerateMembers, PermissionFlagsBits.ManageMessages, PermissionFlagsBits.KickMembers] },
  { key: 'helper', name: 'Помощник', color: 'Grey', hoist: true, canPunish: false, permissions: [PermissionFlagsBits.ManageMessages] },
];

const RANK_LADDER = ['Новичок', 'Сталкер', 'Ветеран', 'Стрелок', 'Охотник', 'Гроза бандитов', 'Мастер Зоны', 'Легенда Зоны'];
// Cumulative XP needed to reach the rank at the same index in RANK_LADDER.
const RANK_THRESHOLDS = [0, 500, 1500, 3500, 7000, 12000, 20000, 35000];

// Game titles stay in the original — they're proper names.
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

const CATEGORIES = {
  STATS: '📊 STAKEOUT',
  START: '📋 НАЧНИ ЗДЕСЬ',
  COMMUNITY: '💬 СООБЩЕСТВО',
  SUPPORT: '🎫 ПОДДЕРЖКА',
  STAFF: '🔐 ПЕРСОНАЛ',
  TICKETS: '🎫 Обращения',
  SERIOUS: '🌍 СЕРЬЁЗНЫЙ РАЗГОВОР',
  OTHER_GAMES: '🎲 ДРУГИЕ ИГРЫ',
  RANKS: '🎖️ ЗВАНИЯ И СОБЫТИЯ',
  AFK: '💤 АФК-ЗОНА',
};

const CHANNELS = {
  WELCOME: 'добро-пожаловать',
  RULES: 'правила',
  ANNOUNCEMENTS: 'объявления',
  ROLES: 'выбор-ролей',
  FAQ: 'вопросы-и-ответы',
  GENERAL: 'общий',
  GAMING_TALK: 'об-играх',
  CLIPS: 'клипы-и-скрины',
  MEMES: 'мемы',
  SELF_PROMO: 'самопиар',
  MUSIC: 'музыка',
  NSFW: 'без-цензуры',
  CREATORS: 'авторы-контента',
  POLITICS: 'политика-и-жизнь',
  TICKET: 'создать-обращение',
  MOD_CHAT: 'чат-модеров',
  MOD_LOGS: 'логи',
  BAN_LIST: 'бан-лист',
  ALT_FLAGS: 'подозрительные',
  BOT_COMMANDS: 'команды-бота',
  LEADERBOARD: 'рейтинг',
  // Same name in every game category (and OTHER GAMES) — the category
  // already says which game, so these are looked up by name + parent.
  GAME_INFO: 'инфо',
  GAME_CHAT: 'чат',
  GAME_SQUAD: 'поиск-отряда',
};

const VOICE = {
  LOUNGE: '🔊 Гостиная',
  CHILL: '🔊 Отдых',
  SQUAD_1: 'Отряд 1',
  SQUAD_2: 'Отряд 2',
  COMMAND: 'Штаб',
  JOIN_TO_CREATE: '➕ Создать канал',
  OTHER_GAMES: '🔊 Другие игры',
  BRIEFING: 'Брифинг',
  AFK: '💤 АФК',
};

const EXTRA_ROLES = [
  { key: 'politics', name: 'Политика', emoji: '🗳️', description: 'Открывает СЕРЬЁЗНЫЙ РАЗГОВОР' },
  { key: 'other-games', name: 'Другие игры', emoji: '🎲', description: 'Открывает категорию ДРУГИЕ ИГРЫ' },
];

const LFG_TAGS = ['Европа', 'Америка', 'Казуал', 'Хардкор', 'Нужны игроки', 'Отряд собран'];

// Manually granted by staff, not self-service via the role panel —
// content creator status is vetted, not a free pick.
const CONTENT_CREATOR_ROLE = { name: 'Автор контента', color: '#D4AF37' };

const BOOSTER_ROLE_NAME = 'Друг STAKEOUT';

module.exports = {
  STAFF_ROLES,
  RANK_LADDER,
  RANK_THRESHOLDS,
  GAMES,
  CATEGORIES,
  CHANNELS,
  VOICE,
  EXTRA_ROLES,
  LFG_TAGS,
  CONTENT_CREATOR_ROLE,
  BOOSTER_ROLE_NAME,
};
