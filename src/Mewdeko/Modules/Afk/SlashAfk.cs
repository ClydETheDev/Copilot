﻿using AngleSharp.Dom;
using System.Collections.Generic;
using Discord;
using Discord.Interactions;
using Humanizer;
using Mewdeko._Extensions;
using Mewdeko.Common;
using Mewdeko.Common.Attributes;
using Mewdeko.Common.Extensions.Interactive;
using Mewdeko.Common.Extensions.Interactive.Entities.Page;
using Mewdeko.Common.Extensions.Interactive.Pagination;
using Mewdeko.Common.Extensions.Interactive.Pagination.Lazy;
using Mewdeko.Common.TypeReaders.Models;
using Mewdeko.Modules.Afk.Services;

namespace Mewdeko.Modules.Afk;
[Group("afk", "Set or Manage afk stuffs")]
public class SlashAfk : MewdekoSlashModuleBase<AfkService>
{
    private readonly InteractiveService _interactivity;

    public SlashAfk(InteractiveService serv) => _interactivity = serv;

    [SlashCommand("set", "Set your afk with an optional message"), RequireContext(ContextType.Guild)]
    public async Task SetAfk(string message = null)
    {
        if (message == null)
        {
            var afkmsg = Service.GetAfkMessage(ctx.Guild.Id, ctx.User.Id).Select(x => x.Message);
            if (!afkmsg.Any() || afkmsg.Last() == "")
            {
                await Service.AfkSet(ctx.Guild, (IGuildUser) ctx.User, "_ _", 0);
                await ctx.Interaction.SendEphemeralConfirmAsync("Afk message enabled!");
                try
                {
                    var user = await ctx.Guild.GetUserAsync(ctx.User.Id);
                    var toset = user.Nickname is null
                        ? $"[AFK] {user.Username.TrimTo(26)}"
                        : $"[AFK] {user.Nickname.TrimTo(26)}";
                    await user.ModifyAsync(x => x.Nickname = toset);
                }
                catch
                {
                    //ignored
                }
                await ctx.Guild.DownloadUsersAsync();
                return;
            }

            await Service.AfkSet(ctx.Guild, (IGuildUser) ctx.User, "", 0);
            await ctx.Interaction.SendEphemeralConfirmAsync("AFK Message has been disabled!");
            try
            {
                var user = await ctx.Guild.GetUserAsync(ctx.User.Id);
                await user.ModifyAsync(x => x.Nickname = user.Nickname.Replace("[AFK]", ""));
            }
            catch
            {
                //ignored
            }
            await ctx.Guild.DownloadUsersAsync();
            return;
        }

        if (message.Length != 0 && message.Length > Service.GetAfkLength(ctx.Guild.Id))
        {
            await ctx.Interaction.SendErrorAsync(
                $"That's too long! The length for afk on this server is set to {Service.GetAfkLength(ctx.Guild.Id)} characters.");
            return;
        }

        await Service.AfkSet(ctx.Guild, (IGuildUser) ctx.User, message, 0);
        await ctx.Interaction.SendConfirmAsync($"AFK Message set to:\n{message}");
        await ctx.Guild.DownloadUsersAsync();
    }

    [SlashCommand("afkmessage", "Allows you to set a custom embed for AFK messages."), 
     RequireContext(ContextType.Guild), 
     RequireUserPermission(GuildPermission.Administrator)]
    public async Task CustomAfkMessage(string embed)
    {
        CrEmbed.TryParse(embed, out var crEmbed);
        if (embed == "-")
        {
            await Service.SetCustomAfkMessage(ctx.Guild, embed);
            await ctx.Interaction.SendConfirmAsync("Afk messages will now have the default look.");
            return;
        }

        if ((crEmbed is not null && !crEmbed.IsValid) || !embed.Contains("%afk"))
        {
            await ctx.Interaction.SendErrorAsync("The embed code you provided cannot be used for afk messages!");
            return;
        }

        await Service.SetCustomAfkMessage(ctx.Guild, embed);
        var ebe = CrEmbed.TryParse(Service.GetCustomAfkMessage(ctx.Guild.Id), out _);
        if (ebe is false)
        {
            await Service.SetCustomAfkMessage(ctx.Guild, "-");
            await ctx.Interaction.SendErrorAsync(
                "There was an error checking the embed, it may be invalid, so I set the afk message back to default. Please dont hesitate to ask for embed help in the support server at https://discord.gg/6n3aa9Xapf.");
            return;
        }

        await ctx.Interaction.SendConfirmAsync("Sucessfully updated afk message!");
    }

    [SlashCommand("listactive", "Sends a list of active afk users"), Usage, Description, Aliases]
    public async Task GetActiveAfks()
    {
        var afks = Service.GetAfkUsers(ctx.Guild);
        if (!afks.Any())
        {
            await ctx.Interaction.SendErrorAsync("There are no currently AFK users!");
            return;
        }

        var paginator = new LazyPaginatorBuilder()
            .AddUser(ctx.User)
            .WithPageFactory(PageFactory)
            .WithFooter(PaginatorFooter.PageNumber | PaginatorFooter.Users)
            .WithMaxPageIndex(afks.ToArray().Length / 20)
            .WithDefaultEmotes()
            .Build();

        await _interactivity.SendPaginatorAsync(paginator, Context.Channel, TimeSpan.FromMinutes(60));

        Task<PageBuilder> PageFactory(int page)
        {
            {
                return Task.FromResult(new PageBuilder().WithOkColor()
                    .WithTitle(Format.Bold("Active AFKs") + $" - {afks.ToArray().Length}")
                    .WithDescription(string.Join("\n", afks.ToArray().Skip(page * 20).Take(20))));
            }
        }
    }

    [SlashCommand("view", "View another user's afk message"), RequireUserPermission(GuildPermission.ManageMessages)]
    public async Task AfkView(IGuildUser user)
    {
        if (!Service.IsAfk(user.Guild, user))
        {
            await ctx.Interaction.SendErrorAsync("This user isn't afk!");
            return;
        }

        var msg = Service.GetAfkMessage(user.Guild.Id, user.Id).Last();
        await ctx.Interaction.SendConfirmAsync($"{user}'s Afk is:\n{msg.Message}");
    }

    [SlashCommand("disabledlist", "Shows a list of channels where afk messages are not allowed to display"), RequireUserPermission(GuildPermission.ManageChannels)]
    public async Task AfkDisabledList()
    {
        var mentions = new List<string>();
        var chans = Service.GetDisabledAfkChannels(ctx.Guild.Id);
        if (string.IsNullOrEmpty(chans) || chans.Contains('0'))
        {
            await ctx.Interaction.SendErrorAsync("You don't have any disabled Afk channels.");
            return;
        }

        await ctx.Interaction.SendConfirmAsync("Loading...");
        var e = chans.Split(",");
        foreach (var i in e)
        {
            var role = await ctx.Guild.GetTextChannelAsync(Convert.ToUInt64(i));
            mentions.Add(role.Mention);
        }

        var paginator = new LazyPaginatorBuilder()
            .AddUser(ctx.User)
            .WithPageFactory(PageFactory)
            .WithFooter(PaginatorFooter.PageNumber | PaginatorFooter.Users)
            .WithMaxPageIndex(mentions.ToArray().Length / 20)
            .WithDefaultEmotes()
            .Build();
        await ctx.Interaction.DeleteOriginalResponseAsync();
        await _interactivity.SendPaginatorAsync(paginator, Context.Channel, TimeSpan.FromMinutes(60));
        
        Task<PageBuilder> PageFactory(int page)
        {
            {
                return Task.FromResult(new PageBuilder().WithOkColor()
                    .WithTitle(Format.Bold("Disabled Afk Channels") + $" - {mentions.ToArray().Length}")
                    .WithDescription(string.Join("\n", mentions.ToArray().Skip(page * 20).Take(20))));
            }
        }
    }

    [SlashCommand("maxlength", "Sets the maximum length of afk messages."), RequireUserPermission(GuildPermission.Administrator)]
    public async Task AfkLength(int num)
    {
        if (num > 4096)
        {
            await ctx.Interaction.SendErrorAsync(
                "The Maximum Length is 4096 per Discord limits. Please put a number lower than that.");
        }
        else
        {
            await Service.AfkLengthSet(ctx.Guild, num);
            await ctx.Interaction.SendConfirmAsync($"AFK Length Sucessfully Set To {num} Characters");
        }
    }

    [SlashCommand("type", "Sets how afk messages are removed. Do @Mewdeko help afktype to see more."), RequireUserPermission(GuildPermission.Administrator)]
    public async Task AfkType(string ehm)
    {
        switch (ehm.ToLower())
        {
            case "onmessage":
            {
                await Service.AfkTypeSet(ctx.Guild, 3);
                await ctx.Interaction.SendConfirmAsync("Afk will be disabled when a user sends a message.");
            }
                break;
            case "ontype":
            {
                await Service.AfkTypeSet(ctx.Guild, 2);
                await ctx.Interaction.SendConfirmAsync("Afk messages will be disabled when a user starts typing.");
            }
                break;
            case "selfdisable":
            {
                await Service.AfkTypeSet(ctx.Guild, 1);
                await ctx.Interaction.SendConfirmAsync(
                    "Afk will only be disableable by the user themselves (unless an admin uses the afkrm command)");
            }
                break;
        }
    }

    [SlashCommand("timeout", "Sets after how long mewdeko no longer ignores a user's typing/messages."), Usage, Description, Aliases, RequireUserPermission(GuildPermission.Administrator)]
    public async Task AfkTimeout(string input)
    {
        var time = StoopidTime.FromInput(input);
        if (time is null)
        {
            await ctx.Interaction.SendEphemeralErrorAsync(
                "The time format provided was incorrect! Please this format: `20m30s`");
            return;
        }
        if (time.Time < TimeSpan.FromSeconds(1) || time.Time > TimeSpan.FromHours(2))
        {
            await ctx.Interaction.SendErrorAsync("The maximum Afk timeout is 2 Hours. Minimum is 1 Second.");
            return;
        }

        await Service.AfkTimeoutSet(ctx.Guild, Convert.ToInt32(time.Time.TotalSeconds));
        await ctx.Interaction.SendConfirmAsync($"Your AFK Timeout has been set to {time.Time.Humanize()}");
    }

    [SlashCommand("undisable", "Allows afk messages to be shown in a channel again."), Usage, Description, Aliases, RequireUserPermission(GuildPermission.ManageChannels)]
    public async Task AfkUndisable(ITextChannel channel)
    {
        var chan = new[] {channel};
        var mentions = new List<string>();
        var toremove = new List<string>();
        var chans = Service.GetDisabledAfkChannels(ctx.Guild.Id);
        if (string.IsNullOrWhiteSpace(chans) || chans == "0")
        {
            await ctx.Interaction.SendErrorAsync("You don't have any disabled channels!");
            return;
        }

        var e = chans.Split(",");
        var list = e.ToList();
        foreach (var i in chan)
            if (e.Contains(i.Id.ToString()))
            {
                toremove.Add(i.Id.ToString());
                mentions.Add(i.Mention);
            }

        if (!mentions.Any())
        {
            await ctx.Interaction.SendErrorAsync("The channels you have specifed are not set to ignore Afk!");
            return;
        }

        if (!list.Except(toremove).Any())
        {
            await Service.AfkDisabledSet(ctx.Guild, "0");
            await ctx.Interaction.SendConfirmAsync("Mewdeko will no longer ignore afk in any channel.");
            return;
        }

        await Service.AfkDisabledSet(ctx.Guild, string.Join(",", list.Except(toremove)));
        await ctx.Interaction.SendConfirmAsync(
            $"Successfully removed the channels {string.Join(",", mentions)} from the list of ignored Afk channels.");
    }

    [SlashCommand("disable", "Disables afk messages to be shown in channels you specify."), Usage, Description, Aliases, RequireUserPermission(GuildPermission.ManageChannels)]
    public async Task AfkDisable(ITextChannel channel)
    {
        var chan = new[] {channel};
        var list = new HashSet<string>();
        var newchans = new HashSet<string>();
        var mentions = new HashSet<string>();
        if (Service.GetDisabledAfkChannels(ctx.Guild.Id) == "0" ||
            string.IsNullOrWhiteSpace(Service.GetDisabledAfkChannels(ctx.Guild.Id)))
        {
            foreach (var i in chan)
            {
                list.Add(i.Id.ToString());
                mentions.Add(i.Mention);
            }

            await Service.AfkDisabledSet(ctx.Guild, string.Join(",", list));
            await ctx.Interaction.SendConfirmAsync(
                $"Afk has been disabled in the channels {string.Join(",", mentions)}");
        }
        else
        {
            var e = Service.GetDisabledAfkChannels(ctx.Guild.Id);
            var w = e.Split(",");
            foreach (var i in w) list.Add(i);

            foreach (var i in chan)
            {
                if (!w.Contains(i.Id.ToString()))
                {
                    list.Add(i.Id.ToString());
                    mentions.Add(i.Mention);
                }

                newchans.Add(i.Id.ToString());
            }

            if (mentions.Any())
            {
                await ctx.Interaction.SendErrorAsync(
                    "No channels were added because the channels you specified are already in the list.");
                return;
            }

            await Service.AfkDisabledSet(ctx.Guild, string.Join(",", list));
            await ctx.Interaction.SendConfirmAsync(
                $"Added {string.Join(",", mentions)} to the list of channels AFK ignores.");
        }
    }

    [SlashCommand("remove", "Removes afk from a user"), RequireUserPermission(GuildPermission.ManageMessages)]
    public async Task AfkRemove(IGuildUser user)
    {
        var msg = Service.GetAfkMessage(ctx.Guild.Id, user.Id).Select(x => x.Message).Last();
        if (msg is null)
        {
            await ctx.Interaction.SendEphemeralErrorAsync("That user isn't afk!");
            return;
        }

        await Service.AfkSet(ctx.Guild, user, "", 0);
        await ctx.Interaction.SendEphemeralConfirmAsync($"AFK Message for {user.Mention} has been disabled!");
    }
}