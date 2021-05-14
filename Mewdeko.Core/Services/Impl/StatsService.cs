﻿using Discord;
using Discord.WebSocket;
using Mewdeko.Extensions;
using NLog;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Mewdeko.Core.Services.Impl
{
    public class StatsService : IStatsService
    {
        private readonly Logger _log;
        private readonly DiscordSocketClient _client;
        private readonly IBotCredentials _creds;
        private readonly DateTime _started;
        private readonly Mewdeko _bot;

        public const string BotVersion = "3.9.4";

        public string Library => "Discord.Net 2.4.0-20210508.2";

        public string Heap => Math.Round((double)GC.GetTotalMemory(false) / 1.MiB(), 2)
            .ToString(CultureInfo.InvariantCulture);
        public double MessagesPerSecond => MessageCounter / GetUptime().TotalSeconds;

        private long _textChannels;
        public long TextChannels => Interlocked.Read(ref _textChannels);
        private long _voiceChannels;
        public long VoiceChannels => Interlocked.Read(ref _voiceChannels);
        private long _messageCounter;
        public long MessageCounter => Interlocked.Read(ref _messageCounter);
        private long _commandsRan;
        public long CommandsRan => Interlocked.Read(ref _commandsRan);

        private readonly Timer _botlistTimer;
        private readonly Timer _botlistTimer2;
        private readonly ConnectionMultiplexer _redis;
        private readonly IHttpClientFactory _httpFactory;

        public StatsService(DiscordSocketClient client, CommandHandler cmdHandler,
            IBotCredentials creds, Mewdeko Mewdeko, IDataCache cache, IHttpClientFactory factory)
        {
            _log = LogManager.GetCurrentClassLogger();
            _client = client;
            _creds = creds;
            _redis = cache.Redis;
            _httpFactory = factory;
            _bot = Mewdeko;

            _started = DateTime.UtcNow;
            _client.MessageReceived += _ => Task.FromResult(Interlocked.Increment(ref _messageCounter));
            cmdHandler.CommandExecuted += (_, e) => Task.FromResult(Interlocked.Increment(ref _commandsRan));

            _client.ChannelCreated += (c) =>
            {
                var _ = Task.Run(() =>
                {
                    if (c is ITextChannel)
                        Interlocked.Increment(ref _textChannels);
                    else if (c is IVoiceChannel)
                        Interlocked.Increment(ref _voiceChannels);
                });

                return Task.CompletedTask;
            };

            _client.ChannelDestroyed += (c) =>
            {
                var _ = Task.Run(() =>
                {
                    if (c is ITextChannel)
                        Interlocked.Decrement(ref _textChannels);
                    else if (c is IVoiceChannel)
                        Interlocked.Decrement(ref _voiceChannels);
                });

                return Task.CompletedTask;
            };

            _client.GuildAvailable += (g) =>
            {
                var _ = Task.Run(() =>
                {
                    var tc = g.Channels.Count(cx => cx is ITextChannel);
                    var vc = g.Channels.Count - tc;
                    Interlocked.Add(ref _textChannels, tc);
                    Interlocked.Add(ref _voiceChannels, vc);
                });
                return Task.CompletedTask;
            };

            _client.JoinedGuild += (g) =>
            {
                var _ = Task.Run(() =>
                {
                    var tc = g.Channels.Count(cx => cx is ITextChannel);
                    var vc = g.Channels.Count - tc;
                    Interlocked.Add(ref _textChannels, tc);
                    Interlocked.Add(ref _voiceChannels, vc);
                });
                return Task.CompletedTask;
            };

            _client.GuildUnavailable += (g) =>
            {
                var _ = Task.Run(() =>
                {
                    var tc = g.Channels.Count(cx => cx is ITextChannel);
                    var vc = g.Channels.Count - tc;
                    Interlocked.Add(ref _textChannels, -tc);
                    Interlocked.Add(ref _voiceChannels, -vc);
                });

                return Task.CompletedTask;
            };

            _client.LeftGuild += (g) =>
            {
                var _ = Task.Run(() =>
                {
                    var tc = g.Channels.Count(cx => cx is ITextChannel);
                    var vc = g.Channels.Count - tc;
                    Interlocked.Add(ref _textChannels, -tc);
                    Interlocked.Add(ref _voiceChannels, -vc);
                });

                return Task.CompletedTask;
            };

            if (_client.ShardId == 0)

                _botlistTimer = new Timer(async (state) =>
                {
                    try
                    {
                        using (var http = _httpFactory.CreateClient())
                        {
                            using (var content = new FormUrlEncodedContent(
                                new Dictionary<string, string> {
                                    { "shard_count",  _creds.TotalShards.ToString()},
                                    { "shard_id", client.ShardId.ToString() },
                                    { "server_count", _bot.GuildCount.ToString() }
                                }))
                            {
                                content.Headers.Clear();
                                content.Headers.Add("Content-Type", "application/x-www-form-urlencoded");
                                http.DefaultRequestHeaders.Add("Authorization", "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpZCI6Ijc1MjIzNjI3NDI2MTQyNjIxMiIsImJvdCI6dHJ1ZSwiaWF0IjoxNjA3Mzg3MDk4fQ.1VATJIr_WqRImXlx5hywaAV6BVk-V4NzybRo0e-E3T8");

                                using (await http.PostAsync(new Uri($"https://top.gg/api/bots/{client.CurrentUser.Id}/stats"), content).ConfigureAwait(false)) { }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex);
                        // ignored
                    }
                }, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));

            _botlistTimer2 = new Timer(async (state) =>
            {
                try
                {
                    using (var http = _httpFactory.CreateClient())
                    {
                        using (var content = new FormUrlEncodedContent(
                            new Dictionary<string, string> {
                                    { "guilds", client.Guilds.Count().ToString() },
                                    { "users", client.Guilds.Sum(x => x.MemberCount).ToString()}

                            }))
                        {
                            content.Headers.Clear();
                            content.Headers.Add("Content-Type", "application/x-www-form-urlencoded");
                            http.DefaultRequestHeaders.Add("Authorization", "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpZCI6Ijc1MjIzNjI3NDI2MTQyNjIxMiIsImJvdCI6dHJ1ZSwiaWF0IjoxNjA3Mzg3MDk4fQ.1VATJIr_WqRImXlx5hywaAV6BVk-V4NzybRo0e-E3T8");

                            using (await http.PostAsync(new Uri($"https://discordbotlist.com/api/v1/{client.CurrentUser.Id}/stats"), content).ConfigureAwait(false)) { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.Error(ex);
                    // ignored
                }
            }, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
        }

        public void Initialize()
        {
            var guilds = _client.Guilds.ToArray();
            _textChannels = guilds.Sum(g => g.Channels.Count(cx => cx is ITextChannel));
            _voiceChannels = guilds.Sum(g => g.Channels.Count(cx => cx is IVoiceChannel));
        }

        public TimeSpan GetUptime() =>
            DateTime.UtcNow - _started;

        public string GetUptimeString(string separator = ", ")
        {
            var time = GetUptime();
            return $"{time.Days} days{separator}{time.Hours} hours{separator}{time.Minutes} minutes";
        }
    }
}
