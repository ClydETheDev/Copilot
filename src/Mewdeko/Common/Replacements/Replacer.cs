﻿using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Mewdeko.Common.Replacements
{
    public class Replacer
    {
        private readonly IEnumerable<(Regex Regex, Func<Match, string> Replacement)> _regex;
        private readonly IEnumerable<(string Key, Func<string> Text)> _replacements;

        public Replacer(IEnumerable<(string, Func<string>)> replacements,
            IEnumerable<(Regex, Func<Match, string>)> regex)
        {
            _replacements = replacements;
            _regex = regex;
        }
        

        public string Replace(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            foreach (var (key, text) in _replacements)
                if (input.Contains(key))
                    input = input.Replace(key, text(), StringComparison.InvariantCulture);

            foreach (var item in _regex) input = item.Regex.Replace(input, m => item.Replacement(m));

            return input;
        }

        public void Replace(CREmbed embedData)
        {
            embedData.PlainText = Replace(embedData.PlainText);
            embedData.Description = Replace(embedData.Description);
            embedData.Title = Replace(embedData.Title);
            embedData.Thumbnail = Replace(embedData.Thumbnail);
            embedData.Image = Replace(embedData.Image);
            if (embedData.Author != null)
            {
                embedData.Author.Name = Replace(embedData.Author.Name);
                embedData.Author.IconUrl = Replace(embedData.Author.IconUrl);
            }

            if (embedData.Fields != null)
                foreach (var f in embedData.Fields)
                {
                    f.Name = Replace(f.Name);
                    f.Value = Replace(f.Value);
                }

            if (embedData.Footer != null)
            {
                embedData.Footer.Text = Replace(embedData.Footer.Text);
                embedData.Footer.IconUrl = Replace(embedData.Footer.IconUrl);
            }
        }
    }
}