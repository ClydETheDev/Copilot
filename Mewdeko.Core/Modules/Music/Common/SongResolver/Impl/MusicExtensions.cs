﻿using System;
using Discord;
using Mewdeko.Extensions;

namespace Mewdeko.Core.Modules.Music
{
    public static class MusicExtensions
    {
        public static string PrettyTotalTime(this IMusicPlayer mp)
        {
            long sum = 0;
            foreach (var track in mp.GetQueuedTracks())
            {
                if (track.Duration == TimeSpan.MaxValue)
                    return "∞";

                sum += track.Duration.Ticks;
            }

            var total = new TimeSpan(sum);

            return total.ToString(@"hh\:mm\:ss");
        }

        public static string PrettyVolume(this IMusicPlayer mp)
        {
            return $"🔉 {(int)(mp.Volume * 100)}%";
        }

        public static string PrettyName(this ITrackInfo trackInfo)
        {
            return $"**[{trackInfo.Title.TrimTo(60).Replace("[", "\\[").Replace("]", "\\]")}]({trackInfo.Url})**";
        }

        public static string PrettyInfo(this IQueuedTrackInfo trackInfo)
        {
            return $"{trackInfo.PrettyTotalTime()} | {trackInfo.Platform} | {trackInfo.Queuer}";
        }

        public static string PrettyFullName(this IQueuedTrackInfo trackInfo)
        {
            return $@"{trackInfo.PrettyName()}
		`{trackInfo.PrettyTotalTime()} | {trackInfo.Platform} | {Format.Sanitize(trackInfo.Queuer.TrimTo(15))}`";
        }

        public static string PrettyTotalTime(this ITrackInfo trackInfo)
        {
            if (trackInfo.Duration == TimeSpan.Zero)
                return "(?)";
            if (trackInfo.Duration == TimeSpan.MaxValue)
                return "∞";
            if (trackInfo.Duration.TotalHours >= 1)
                return trackInfo.Duration.ToString(@"hh\:mm\:ss");

            return trackInfo.Duration.ToString(@"mm\:ss");
        }

        public static ICachableTrackData ToCachedData(this ITrackInfo trackInfo, string id)
        {
            return new CachableTrackData
            {
                TotalDurationMs = trackInfo.Duration.TotalMilliseconds,
                Id = id,
                Thumbnail = trackInfo.Thumbnail,
                Url = trackInfo.Url,
                Platform = trackInfo.Platform,
                Title = trackInfo.Title
            };
        }
    }
}