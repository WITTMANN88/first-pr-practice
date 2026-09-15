// A separate currency ("coins") from XP — XP drives rank/prestige,
// coins are just for fun (daily claim, coinflip, paying other people).
const fs = require('fs');
const path = require('path');

const DATA_PATH = path.join(__dirname, '..', 'data', 'economy.json');
const DAILY_COOLDOWN_MS = 24 * 60 * 60 * 1000;
const DAILY_MIN = 50;
const DAILY_MAX = 150;
const PREFIX = '!';

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

function getEntry(userId) {
  return store[userId] ?? { balance: 0, lastDaily: 0 };
}

function getBalance(userId) {
  return getEntry(userId).balance;
}

function addCoins(userId, amount) {
  const entry = getEntry(userId);
  entry.balance += amount;
  store[userId] = entry;
  save();
  return entry.balance;
}

function getTopBalances(limit) {
  return Object.entries(store)
    .map(([userId, entry]) => ({ userId, balance: entry.balance }))
    .filter((e) => e.balance > 0)
    .sort((a, b) => b.balance - a.balance)
    .slice(0, limit);
}

async function handleMessage(message) {
  if (message.author.bot || !message.guild) return;
  if (!message.content.startsWith(PREFIX)) return;

  const [cmdRaw, ...rest] = message.content.slice(PREFIX.length).trim().split(/\s+/);
  const cmd = cmdRaw?.toLowerCase();
  const userId = message.author.id;

  switch (cmd) {
    case 'daily': {
      const entry = getEntry(userId);
      const now = Date.now();
      if (now - entry.lastDaily < DAILY_COOLDOWN_MS) {
        const hoursLeft = Math.ceil((DAILY_COOLDOWN_MS - (now - entry.lastDaily)) / 3_600_000);
        return void message.reply(`Уже забирал сегодня — заходи через ${hoursLeft}ч.`);
      }
      const amount = DAILY_MIN + Math.floor(Math.random() * (DAILY_MAX - DAILY_MIN + 1));
      entry.balance += amount;
      entry.lastDaily = now;
      store[userId] = entry;
      save();
      message.reply(`🪙 Получено ${amount} монет. Баланс: ${entry.balance}.`);
      break;
    }
    case 'balance': {
      const target = message.mentions.users?.first() ?? message.author;
      message.reply(`🪙 Баланс ${target.tag}: ${getBalance(target.id)} монет.`);
      break;
    }
    case 'coinflip': {
      const amount = Number(rest[0]);
      if (!amount || amount < 1) return void message.reply('Укажи ставку: `!coinflip 50`');
      if (amount > getBalance(userId)) return void message.reply('Не хватает монет.');
      const win = Math.random() < 0.5;
      const balance = addCoins(userId, win ? amount : -amount);
      message.reply(win ? `🪙 Орёл! +${amount}. Баланс: ${balance}.` : `💀 Решка. -${amount}. Баланс: ${balance}.`);
      break;
    }
    case 'pay': {
      const target = message.mentions.users?.first();
      const amount = Number(rest[1]);
      if (!target || !amount || amount < 1) return void message.reply('Формат: `!pay @user 50`');
      if (target.id === userId) return void message.reply('Себе платить незачем.');
      if (amount > getBalance(userId)) return void message.reply('Не хватает монет.');
      addCoins(userId, -amount);
      const newBalance = addCoins(target.id, amount);
      message.reply(`🪙 Переведено ${amount} монет ${target.tag} (у него теперь ${newBalance}).`);
      break;
    }
    default:
      break;
  }
}

load();

module.exports = { handleMessage, getBalance, getTopBalances };
