﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;

namespace Mewdeko._Extensions
{
    public static class MessageChannelExtensions
    {

        public static Task<IUserMessage> EmbedAsync(this IMessageChannel ch, EmbedBuilder embed, string msg = "")
        {
            return ch.SendMessageAsync(msg, embed: embed.Build(),
                options: new RequestOptions { RetryMode = RetryMode.AlwaysRetry });
        }

        public static Task<IUserMessage> SendErrorAsync(this IMessageChannel ch, string title, string error,
            string url = null, string footer = null)
        {
            var eb = new EmbedBuilder().WithErrorColor().WithDescription(error)
                .WithTitle(title);
            if (url != null && Uri.IsWellFormedUriString(url, UriKind.Absolute))
                eb.WithUrl(url);
            if (!string.IsNullOrWhiteSpace(footer))
                eb.WithFooter(efb => efb.WithText(footer));
            return ch.SendMessageAsync("", embed: eb.Build());
        }

        public static Task<IUserMessage> SendErrorAsync(this IMessageChannel ch, string error)
        {
            return ch.SendMessageAsync("", embed: new EmbedBuilder().WithErrorColor().WithDescription(error).Build());
        }

        public static Task<IUserMessage> SendConfirmAsync(this IMessageChannel ch, string title, string text,
            string url = null, string footer = null)
        {
            var eb = new EmbedBuilder().WithOkColor().WithDescription(text)
                .WithTitle(title);
            if (url != null && Uri.IsWellFormedUriString(url, UriKind.Absolute))
                eb.WithUrl(url);
            if (!string.IsNullOrWhiteSpace(footer))
                eb.WithFooter(efb => efb.WithText(footer));
            return ch.SendMessageAsync("", embed: eb.Build());
        }

        public static Task<IUserMessage> SendConfirmAsync(this IMessageChannel ch, string text, ComponentBuilder builder = null)
        {
            return ch.SendMessageAsync(embed: new EmbedBuilder().WithOkColor().WithDescription(text).Build(), component: builder?.Build());
        }

        public static Task<IUserMessage> SendTableAsync<T>(this IMessageChannel ch, string seed, IEnumerable<T> items,
            Func<T, string> howToPrint, int columns = 3)
        {
            var i = 0;
            return ch.SendMessageAsync($@"{seed}```css
{string.Join("\n", items.GroupBy(_ => i++ / columns)
    .Select(ig => string.Concat(ig.Select(howToPrint))))}
```");
        }

        public static Task<IUserMessage> SendTableAsync<T>(this IMessageChannel ch, IEnumerable<T> items,
            Func<T, string> howToPrint, int columns = 3)
        {
            return ch.SendTableAsync("", items, howToPrint, columns);
        }

        public static Task OkAsync(this ICommandContext ctx)
        {
            return ctx.Message.AddReactionAsync(new Emoji("✅"));
        }

        public static Task ErrorAsync(this ICommandContext ctx)
        {
            return ctx.Message.AddReactionAsync(new Emoji("❌"));
        }

        public static Task WarningAsync(this ICommandContext ctx)
        {
            return ctx.Message.AddReactionAsync(new Emoji("⚠"));
        }
    }
}