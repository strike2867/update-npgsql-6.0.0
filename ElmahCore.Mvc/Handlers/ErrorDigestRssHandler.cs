using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using ElmahCore.Mvc.Notifiers;
using ElmahCore.Mvc.Xml;
using Microsoft.AspNetCore.Http;

namespace ElmahCore.Mvc.Handlers
{
    /// <summary>
    ///     Renders an RSS feed that is a daily digest of the most recently
    ///     recorded errors in the error log. The feed spans at most 15
    ///     days on which errors occurred.
    /// </summary>
    internal static class ErrorDigestRssHandler
    {
        public static async Task ProcessRequest(HttpContext context, ErrorLog errorLog, string elmahRoot)
        {
            var log = errorLog;

            var response = context.Response;

            response.ContentType = "application/xml";

            var title = $@"Daily digest of errors in {log.ApplicationName} on {Environment.MachineName}";

            var link = $"{context.Request.Scheme}://{context.Request.PathBase.ToString().TrimEnd('/')}{elmahRoot}";
            var baseUrl = new Uri(link.TrimEnd('/') + "/");

            var items = GetItems(log, baseUrl, 30, 30).Take(30);
            var rss = RssXml.Rss(title, link, "Daily digest of application errors", items);

            await context.Response.WriteAsync(XmlText.StripIllegalXmlCharacters(rss.ToString()));
        }

        private static IEnumerable<XElement> GetItems(ErrorLog log, Uri baseUrl, int pageSize, int maxPageLimit)
        {
            Debug.Assert(log != null);
            Debug.Assert(baseUrl != null);
            Debug.Assert(baseUrl.IsAbsoluteUri);
            Debug.Assert(pageSize > 0);

            var runningDay = DateTime.MaxValue;
            var runningErrorCount = 0;
            string title = null;
            DateTime? pubDate = null;
            var sb = new StringBuilder();
            var writer = new StringWriter(sb);

            var source = GetErrors(log, pageSize, (p, e) => new {PageIndex = p, Entry = e});

            foreach (var entry in from item in source.TakeWhile(e => e.PageIndex < maxPageLimit)
                select item.Entry)
            {
                var error = entry.Error;
                var time = error.Time.ToUniversalTime();
                var day = time.Date;

                //
                // If we're dealing with a new day then break out to a 
                // new channel item, finishing off the previous one.
                //

                if (day < runningDay)
                {
                    if (runningErrorCount > 0)
                    {
                        RenderEnd(writer);
                        Debug.Assert(title != null);
                        Debug.Assert(pubDate != null);
                        yield return RssXml.Item(title, sb.ToString(), pubDate.Value);
                    }

                    runningDay = day;
                    runningErrorCount = 0;
                    pubDate = time;
                    title = $"Digest for {runningDay:yyyy-MM-dd} ({runningDay.ToLongDateString()})";
                    sb.Length = 0;
                    RenderStart(writer);
                }

                RenderError(writer, entry, baseUrl);
                runningErrorCount++;
            }

            if (runningErrorCount > 0)
            {
                RenderEnd(writer);
                Debug.Assert(title != null);
                Debug.Assert(pubDate != null);
                yield return RssXml.Item(title, sb.ToString(), pubDate.Value);
            }
        }

        private static IEnumerable<T> GetErrors<T>(ErrorLog log, int pageSize, Func<int, ErrorLogEntry, T> resultor)
        {
            Debug.Assert(log != null);
            Debug.Assert(pageSize > 0);
            Debug.Assert(resultor != null);

            var entries = new List<ErrorLogEntry>(pageSize);
            for (var pageIndex = 0;; pageIndex++)
            {
                log.GetErrors(null, new List<ErrorLogFilter>(), pageIndex, pageSize, entries);
                if (!entries.Any())
                    break;
                foreach (var entry in entries)
                    yield return resultor(pageIndex, entry);
                entries.Clear();
            }
        }

        // TODO Consider moving the rest to a Razor template

        private static void RenderStart(TextWriter writer)
        {
            Debug.Assert(writer != null);

            writer.Write("<ul>");
        }

        private static void RenderError(TextWriter writer, ErrorLogEntry entry, Uri baseUrl)
        {
            Debug.Assert(writer != null);
            Debug.Assert(entry != null);
            Debug.Assert(baseUrl != null);
            Debug.Assert(baseUrl.IsAbsoluteUri);

            var error = entry.Error;
            writer.Write("<li>");

            var errorType = ErrorDisplay.HumaneExceptionErrorType(error);

            if (errorType.Length > 0)
            {
                var abbreviated = errorType.Length < error.Type.Length;

                if (abbreviated)
                    writer.Write("<span title='{0}'>", WebUtility.HtmlEncode(error.Type));

                writer.Write(WebUtility.HtmlEncode(errorType));

                if (abbreviated)
                    writer.Write("</span>");

                writer.Write(": ");
            }

            writer.Write("<a href='{0}'>",
                WebUtility.HtmlEncode(baseUrl + "detail?id=" + Uri.EscapeDataString(entry.Id)));
            writer.Write(WebUtility.HtmlEncode(error.Message));
            writer.Write("</a>");

            writer.Write("</li>");
        }

        private static void RenderEnd(TextWriter writer)
        {
            Debug.Assert(writer != null);

            writer.Write("</li>");
            writer.Flush();
        }
    }
}