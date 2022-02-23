﻿using System.IO;
using System.Net.Http;
using Mewdeko.Common;
using Serilog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Mewdeko.Modules.Games.Common;

public class GirlRating
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IImageCache _images;

    public GirlRating(IImageCache images, IHttpClientFactory factory, double crazy, double hot, int roll,
        string advice)
    {
        _images = images;
        Crazy = crazy;
        Hot = hot;
        Roll = roll;
        Advice = advice; // convenient to have it here, even though atm there are only few different ones.
        _httpFactory = factory;

        Stream = new AsyncLazy<Stream>(() =>
        {
            try
            {
                using var img = SixLabors.ImageSharp.Image.Load(_images.RategirlMatrix);
                const int minx = 35;
                const int miny = 385;
                const int length = 345;

                var pointx = (int) (minx + (length * (Hot / 10)));
                var pointy = (int) (miny - (length * ((Crazy - 4) / 6)));

                using (var pointImg = SixLabors.ImageSharp.Image.Load(_images.RategirlDot))
                {
                    img.Mutate(x =>
                        x.DrawImage(pointImg, new Point(pointx - 10, pointy - 10), new GraphicsOptions()));
                }

                var imgStream = new MemoryStream();
                img.SaveAsPng(imgStream);
                return imgStream;
                //using (var byteContent = new ByteArrayContent(imgStream.ToArray()))
                //{
                //    http.AddFakeHeaders();

                //    using (var reponse = await http.PutAsync("https://transfer.sh/img.png", byteContent).ConfigureAwait(false))
                //    {
                //        url = await reponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                //    }
                //}
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error getting RateGirl image");
                return null;
            }
        });
    }

    public double Crazy { get; }
    public double Hot { get; }
    public int Roll { get; }
    public string Advice { get; }

    public AsyncLazy<Stream> Stream { get; }
}