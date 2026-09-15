// Music: text commands in #music-commands (no slash-command deploy
// step needed). Handles YouTube/SoundCloud links, search-by-name, and
// Spotify links (Spotify is metadata-only — playback is bridged to
// YouTube/SoundCloud, since Spotify never allows raw audio streaming).
// YouTube extraction is inherently fragile — it's YouTube fighting
// scrapers, not a bug in this code — SoundCloud is the reliable fallback
// if YouTube playback ever stops working.
const { EmbedBuilder } = require('discord.js');
const { Player } = require('discord-player');
const { DefaultExtractors } = require('@discord-player/extractor');

const PREFIX = '!';
const HELP_MARKER = 'stakeout-music-help-v1';

let player;

function getPlayer(client) {
  if (player) return player;

  player = new Player(client);
  player.extractors.loadMulti(DefaultExtractors).catch((err) => {
    console.error('Failed to load music extractors:', err);
  });

  player.events.on('playerStart', (queue, track) => {
    queue.metadata?.channel?.send(`▶️ Сейчас играет: **${track.title}** — ${track.author}`).catch(() => {});
  });
  player.events.on('audioTrackAdd', (queue, track) => {
    queue.metadata?.channel?.send(`➕ В очередь: **${track.title}**`).catch(() => {});
  });
  player.events.on('emptyChannel', (queue) => {
    queue.metadata?.channel?.send('Все вышли из войса — ухожу.').catch(() => {});
  });
  player.events.on('error', (queue, error) => {
    console.error('Player error:', error);
  });
  player.events.on('playerError', (queue, error) => {
    console.error('Playback error:', error);
    queue.metadata?.channel?.send('Не смог это сыграть — пробую следующее в очереди.').catch(() => {});
  });

  return player;
}

async function postMusicHelp(guild) {
  const channel = guild.channels.cache.find((c) => c.name === 'music-commands');
  if (!channel) return;

  const recent = await channel.messages.fetch({ limit: 20 });
  const already = recent.find(
    (m) => m.author.id === guild.client.user.id && m.embeds[0]?.footer?.text === HELP_MARKER,
  );
  if (already) return;

  const embed = new EmbedBuilder()
    .setTitle('Команды музыки')
    .setDescription(
      [
        '`!play <ссылка или название>` — играть / добавить в очередь',
        '`!skip` — пропустить трек',
        '`!pause` / `!resume` — пауза / продолжить',
        '`!stop` — остановить и выйти из войса',
        '`!queue` — показать очередь',
        '',
        'Работает с YouTube и SoundCloud напрямую; Spotify-ссылки тоже принимаются — сам трек ищется на YouTube/SoundCloud, Spotify отдаёт только название.',
      ].join('\n'),
    )
    .setColor(0x8b0000)
    .setFooter({ text: HELP_MARKER });

  await channel.send({ embeds: [embed] });
  console.log('Posted music help in #music-commands');
}

async function handleMessage(message) {
  if (message.author.bot || !message.guild) return;
  if (message.channel.name !== 'music-commands') return;
  if (!message.content.startsWith(PREFIX)) return;

  const [cmdRaw, ...rest] = message.content.slice(PREFIX.length).trim().split(/\s+/);
  const cmd = cmdRaw?.toLowerCase();
  const query = rest.join(' ');
  const musicPlayer = getPlayer(message.client);
  const queue = musicPlayer.nodes.get(message.guild.id);

  switch (cmd) {
    case 'play': {
      const voiceChannel = message.member?.voice?.channel;
      if (!voiceChannel) return void message.reply('Зайди сначала в голосовой канал.');
      if (!query) return void message.reply('Напиши название или ссылку: `!play <что играть>`');
      try {
        await musicPlayer.play(voiceChannel, query, {
          nodeOptions: {
            metadata: { channel: message.channel },
            leaveOnEmpty: true,
            leaveOnEmptyCooldown: 60_000,
            leaveOnEnd: true,
            leaveOnEndCooldown: 60_000,
            volume: 50,
          },
        });
      } catch (err) {
        console.error('Play failed:', err);
        message.reply('Не нашёл это — попробуй другую ссылку или формулировку.');
      }
      break;
    }
    case 'skip': {
      if (!queue?.currentTrack) return void message.reply('Сейчас ничего не играет.');
      queue.node.skip();
      message.reply('⏭️ Пропустил.');
      break;
    }
    case 'stop': {
      if (!queue) return void message.reply('Сейчас ничего не играет.');
      queue.delete();
      message.reply('⏹️ Остановил и вышел.');
      break;
    }
    case 'pause': {
      if (!queue?.currentTrack) return void message.reply('Сейчас ничего не играет.');
      queue.node.setPaused(true);
      message.reply('⏸️ Пауза.');
      break;
    }
    case 'resume': {
      if (!queue) return void message.reply('Сейчас ничего не играет.');
      queue.node.setPaused(false);
      message.reply('▶️ Продолжаю.');
      break;
    }
    case 'queue': {
      if (!queue?.currentTrack) return void message.reply('Очередь пуста.');
      const upcoming =
        queue.tracks
          .toArray()
          .slice(0, 10)
          .map((t, i) => `${i + 1}. ${t.title} — ${t.author}`)
          .join('\n') || '_дальше пусто_';
      message.reply(`Сейчас: **${queue.currentTrack.title}**\n\n${upcoming}`);
      break;
    }
    default:
      break;
  }
}

module.exports = { postMusicHelp, handleMessage };
