using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Raphael.Tourmaline
{
    internal static class Helpers
    {
        internal static Regex JSPathFinder { get; } = new(@"['""](\/?[a-zA-Z0-9\-_]+(?:\/[a-zA-Z0-9\-_\.]+)+(?:\?[a-zA-Z0-9=&%\-_\.]+)?)['""]");
        internal static Regex HTMLPathFinder { get; } = new(@"(?:src|href|action)=['""]([^'""]+)['""]");
        internal static Regex OtherPathFinder { get; } = new(@"(\/[a-zA-Z0-9\-_]+|[a-zA-Z0-9\-_]+\.[a-zA-Z0-9\-_]+)");

        internal static List<string> SpiderMatch(string content, Uri baseUri)
        {
            MatchCollection jsMatches = JSPathFinder.Matches(content);
            MatchCollection htmlMatches = HTMLPathFinder.Matches(content);
            //MatchCollection otherMatches = OtherPathFinder.Matches(content);
            List<string> matches = [];

            foreach (Match match in htmlMatches.Concat(jsMatches))
            {
                string processed = ProcessUrl(match.Groups[1].Value, baseUri);
                if (!string.IsNullOrEmpty(processed)) matches.Add(processed);
            }

            return matches;
        }

        internal static string ProcessUrl(string url, Uri baseUri)
        {
            if (Uri.TryCreate(baseUri, url, out Uri? result))
                return result.GetLeftPart(UriPartial.Path);
            return string.Empty;
        }

        internal static bool CheckDepth(string path, int maxDepth)
        {
            if (maxDepth == -1) return true;
            return path.Count(c => c == '/') <= maxDepth;
        }

        internal static string ResolveInitialUrl(string baseUrl)
        {
            if (!baseUrl.StartsWith("http")) baseUrl = "http://" + baseUrl;
            return baseUrl;
        }
    }
}
