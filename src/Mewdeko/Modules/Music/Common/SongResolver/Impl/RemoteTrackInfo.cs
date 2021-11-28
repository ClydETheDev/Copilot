﻿#nullable enable
using System;
using System.Threading.Tasks;

namespace Mewdeko.Modules.Music.Common.SongResolver.Impl
{
    public sealed class RemoteTrackInfo : ITrackInfo
    {
        private readonly Func<Task<string?>> _streamFactory;

        public RemoteTrackInfo(string title, string url, string thumbnail, TimeSpan duration, MusicPlatform platform,
            Func<Task<string?>> streamFactory)
        {
            _streamFactory = streamFactory;
            Title = title;
            Url = url;
            Thumbnail = thumbnail;
            Duration = duration;
            Platform = platform;
        }

        public string Title { get; }
        public string Url { get; }
        public string Thumbnail { get; }
        public TimeSpan Duration { get; }
        public MusicPlatform Platform { get; }

        public async ValueTask<string?> GetStreamUrl()
        {
            return await _streamFactory();
        }
    }
}