﻿using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using Mewdeko._Extensions;
using Mewdeko.Database;
using Mewdeko.Database.Extensions;
using Mewdeko.Modules.Utility.Common;
using VirusTotalNet;
using VirusTotalNet.Results;

namespace Mewdeko.Modules.Utility.Services;

public class UtilityService : INService
{
    private readonly DiscordSocketClient _client;
    private readonly DbService _db;
    private readonly IDataCache _cache;
    public UtilityService(DiscordSocketClient client, DbService db, Mewdeko bot,
        IDataCache cache)
    {
        _client = client;
        client.MessageDeleted += MsgStore;
        client.MessageUpdated += MsgStore2;
        client.MessageReceived += MsgReciev;
        client.MessageReceived += MsgReciev2;
        client.MessagesBulkDeleted += BulkMsgStore;
        _db = db;
        _cache = cache;
        Snipeset = bot.AllGuildConfigs
                      .ToDictionary(x => x.GuildId, x => x.snipeset)
                      .ToConcurrent();
        Plinks = bot.AllGuildConfigs
                    .ToDictionary(x => x.GuildId, x => x.PreviewLinks)
                    .ToConcurrent();
        Reactchans = bot.AllGuildConfigs
                        .ToDictionary(x => x.GuildId, x => x.ReactChannel)
                        .ToConcurrent();
        _ = PruneTimer();

    }

    private ConcurrentDictionary<ulong, bool> Snipeset { get; }
    private ConcurrentDictionary<ulong, int> Plinks { get; }
    private ConcurrentDictionary<ulong, ulong> Reactchans { get; }
    public async Task PruneTimer()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync())
        {
            var guild = _client.GetGuild(708154079695601685);
            var channel = guild.GetTextChannel(946933865866493983);
            var messages = (await channel.GetMessagesAsync(1000).FlattenAsync())?.Where(x =>
                DateTimeOffset.Now.Subtract(x.Timestamp).TotalSeconds >= TimeSpan.FromSeconds(5).TotalSeconds);
            if (!messages.Any())
                return;
            await channel.DeleteMessagesAsync(messages);
        }
    }
    
    public async Task<List<SnipeStore>> GetSnipes(ulong guildId) 
        => await _cache.GetSnipesForGuild(guildId);
    public int GetPLinks(ulong? id)
    {
        if (id == null || !Plinks.TryGetValue(id.Value, out var invw))
            return 0;

        return invw;
    }

    public ulong GetReactChans(ulong? id)
    {
        if (id == null || !Reactchans.TryGetValue(id.Value, out var invw))
            return 0;

        return invw;
    }

    public async Task SetReactChan(IGuild guild, ulong yesnt)
    {
        await using (var uow = _db.GetDbContext())
        {
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.ReactChannel = yesnt;
            await uow.SaveChangesAsync();
        }

        Reactchans.AddOrUpdate(guild.Id, yesnt, (_, _) => yesnt);
    }

    public async Task PreviewLinks(IGuild guild, string yesnt)
    {
        var yesno = -1;
        await using (_db.GetDbContext())
        {
            yesno = yesnt switch
            {
                "y" => 1,
                "n" => 0,
                _ => yesno
            };
        }

        await using (var uow = _db.GetDbContext())
        {
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.PreviewLinks = yesno;
            await uow.SaveChangesAsync();
        }

        Plinks.AddOrUpdate(guild.Id, yesno, (_, _) => yesno);
    }

    public bool GetSnipeSet(ulong? id)
    {
        Snipeset.TryGetValue(id.Value, out var snipeset);
        return snipeset;
    }

    public async Task SnipeSet(IGuild guild, string endis)
    {
        var yesno = endis == "enable";
        await using (var uow = _db.GetDbContext())
        {
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.snipeset = yesno;
            await uow.SaveChangesAsync();
        }

        Snipeset.AddOrUpdate(guild.Id, yesno, (_, _) => yesno);
    }
    public async Task SnipeSetBool(IGuild guild, bool enabled)
    {
        await using (var uow = _db.GetDbContext())
        {
            var gc = uow.ForGuildId(guild.Id, set => set);
            gc.snipeset = enabled;
            await uow.SaveChangesAsync();
        }

        Snipeset.AddOrUpdate(guild.Id, enabled, (_, _) => enabled);
    }

    private async Task BulkMsgStore(
        IReadOnlyCollection<Cacheable<IMessage, ulong>> messages,
        Cacheable<IMessageChannel, ulong> channel)
    {
        if (!channel.HasValue)
            return;

        if (channel.Value is not SocketTextChannel chan)
            return;

        if (!GetSnipeSet(chan.Guild.Id))
            return;

        if (!messages.Select(x => x.HasValue).Any())
            return;

        var msgs = messages.Where(x => x.HasValue).Select(x => new SnipeStore()
        {
            GuildId = chan.Guild.Id,
            ChannelId = chan.Id,
            Message = x.Value.Content,
            UserId = x.Value.Author.Id,
            Edited = 0,
            DateAdded = DateTime.UtcNow
        });
        var snipes = await _cache.GetSnipesForGuild(chan.Guild.Id) ?? new List<SnipeStore>();
        if (!snipes.Any())
        {
            var todelete = snipes.Where(x => DateTime.UtcNow.Subtract(x.DateAdded) >= TimeSpan.FromDays(3));
            if (todelete.Any())
                snipes.RemoveRange(todelete);
        }
        snipes.AddRange(msgs);
        await _cache.AddSnipeToCache(chan.Guild.Id, snipes);
    }

    private Task MsgStore(Cacheable<IMessage, ulong> optMsg, Cacheable<IMessageChannel, ulong> ch)
    {
        _ = Task.Run(async () =>
        {
            if (!GetSnipeSet(((SocketTextChannel) ch.Value).Guild.Id)) return;

            if ((optMsg.HasValue ? optMsg.Value : null) is not IUserMessage msg || msg.Author.IsBot) return;
            var user = await msg.Channel.GetUserAsync(optMsg.Value.Author.Id);
            if (user is null) return;
            if (!user.IsBot)
            {
                var snipemsg = new SnipeStore
                {
                    GuildId = ((SocketTextChannel) ch.Value).Guild.Id,
                    ChannelId = ch.Id,
                    Message = msg.Content,
                    UserId = msg.Author.Id,
                    Edited = 0,
                    DateAdded = DateTime.UtcNow
                };
                var snipes = await _cache.GetSnipesForGuild(((SocketTextChannel) ch.Value).Guild.Id) ?? new List<SnipeStore>();
                if (!snipes.Any())
                {
                    var todelete = snipes.Where(x => DateTime.UtcNow.Subtract(x.DateAdded) >= TimeSpan.FromDays(3));
                    if (todelete.Any())
                        snipes.RemoveRange(todelete);
                }
                snipes.Add(snipemsg);
                await _cache.AddSnipeToCache(((SocketTextChannel) ch.Value).Guild.Id, snipes);
            }
        });
        return Task.CompletedTask;
    }

    private Task MsgStore2(Cacheable<IMessage, ulong> optMsg, SocketMessage imsg2,
        ISocketMessageChannel ch)
    {
        _ = Task.Run(async () =>
        {
            if (ch is not ITextChannel)
                return;
            
            if (!GetSnipeSet(((SocketTextChannel) ch).Guild.Id)) return;

            if ((optMsg.HasValue ? optMsg.Value : null) is not IUserMessage msg || msg.Author.IsBot) return;
            var user = await msg.Channel.GetUserAsync(msg.Author.Id);
            if (user is null) return;
            if (!user.IsBot)
            {
                var snipemsg = new SnipeStore
                {
                    GuildId = ((SocketTextChannel) ch).Guild.Id,
                    ChannelId = ch.Id,
                    Message = msg.Content,
                    UserId = msg.Author.Id,
                    Edited = 1,
                    DateAdded = DateTime.UtcNow
                };
                var snipes = await _cache.GetSnipesForGuild(((SocketTextChannel) ch).Guild.Id) ?? new List<SnipeStore>();
                if (!snipes.Any())
                {
                    var todelete = snipes.Where(x => DateTime.UtcNow.Subtract(x.DateAdded) >= TimeSpan.FromDays(3));
                    if (todelete.Any())
                        snipes.RemoveRange(todelete);
                }
                snipes.Add(snipemsg);
                await _cache.AddSnipeToCache(((SocketTextChannel) ch).Guild.Id, snipes);
            }
        });
        return Task.CompletedTask;
    }

    public async Task MsgReciev2(SocketMessage msg)
    {
        if (msg.Author.IsBot) return;
        if (msg.Channel is SocketDMChannel) return;
        var guild = ((SocketGuildChannel) msg.Channel).Guild.Id;
        var id = GetReactChans(guild);
        if (msg.Channel.Id == id)
        {
            Emote.TryParse("<:upvote:863122283283742791>", out var emote);
            Emote.TryParse("<:D_downvote:863122244527980613>", out var emote2);
            await Task.Delay(200);
            await msg.AddReactionAsync(emote);
            await Task.Delay(200);
            await msg.AddReactionAsync(emote2);
        }
    }

    public static async Task<UrlReport> UrlChecker(string url)
    {
        var vcheck = new VirusTotal("e49046afa41fdf4e8ca72ea58a5542d0b8fbf72189d54726eed300d2afe5d9a9");
        return await vcheck.GetUrlReportAsync(url, true);
    }

    public async Task MsgReciev(SocketMessage msg)
    {
        if (msg.Channel is SocketTextChannel t)
        {
            if (msg.Author.IsBot) return;
            var gid = t.Guild;
            if (GetPLinks(gid.Id) == 1)
            {
                var linkParser =
                    new Regex(
                        @"https?:\/\/(www\.)?[-a-zA-Z0-9@:%._\+~#=]{1,256}\.[a-zA-Z0-9()]{1,6}\b([-a-zA-Z0-9()@:%_\+.~#?&//=]*)",
                        RegexOptions.Compiled | RegexOptions.IgnoreCase);
                foreach (Match m in linkParser.Matches(msg.Content))
                {
                    var e = new Uri(m.Value);
                    var en = e.Host.Split(".");
                    if (!en.Contains("discord")) continue;
                    var eb = string.Join("", e.Segments).Split("/");
                    if (!eb.Contains("channels")) continue;
                    SocketGuild guild;
                    if (gid.Id != Convert.ToUInt64(eb[2]))
                    {
                        guild = _client.GetGuild(Convert.ToUInt64(eb[2]));
                        if (guild is null) return;
                    }
                    else
                    {
                        guild = gid;
                    }

                    if (guild != t.Guild)
                        return;
                    var em = await ((IGuild) guild).GetTextChannelAsync(Convert.ToUInt64(eb[3]));
                    if (em == null) return;
                    var msg2 = await em.GetMessageAsync(Convert.ToUInt64(eb[4]));
                    if (msg2 is null) return;
                    var en2 = new EmbedBuilder
                    {
                        Color = Mewdeko.OkColor,
                        Author = new EmbedAuthorBuilder
                        {
                            Name = msg2.Author.Username,
                            IconUrl = msg2.Author.GetAvatarUrl(size: 2048)
                        },
                        Footer = new EmbedFooterBuilder
                        {
                            IconUrl = ((IGuild) guild).IconUrl,
                            Text = $"{((IGuild) guild).Name}: {em.Name}"
                        }
                    };
                    if (msg2.Embeds.Any())
                    {
                        en2.AddField("Embed Content:", msg2.Embeds.FirstOrDefault()?.Description);
                        if (msg2.Embeds.FirstOrDefault()!.Image != null)
                            en2.ImageUrl = msg2.Embeds.FirstOrDefault()?.Image.Value.Url;
                    }

                    if (msg2.Content.Any()) en2.Description = msg2.Content;

                    if (msg2.Attachments.Any()) en2.ImageUrl = msg2.Attachments.FirstOrDefault().Url;

                    await msg.Channel.SendMessageAsync("",
                        embed: en2.WithTimestamp(msg2.Timestamp).Build());
                }
            }
        }
    }
}
