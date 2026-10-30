using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;
using System.Web.Script.Serialization;
using GoldWidget;

internal static class QuoteTests
{
    private static readonly DateTimeOffset Now =
        new DateTimeOffset(2026, 10, 29, 7, 17, 54, TimeSpan.Zero);
    private static int assertions;

    private static int Main(string[] args)
    {
        try
        {
            GoldQuote quote = GoldQuote.Parse(Payload(), Now);
            Check(quote.BuyPrice == 868.19m, "CCB active buy price");
            Check(quote.RedemptionPrice == 862.19m, "CCB redemption field, not a guessed spread");
            Check(quote.Change == -2.38m, "change from CCB previous close");
            Check(quote.ChangePercent == -0.27m, "percentage from CCB previous close");
            Check(quote.TimestampUtc == Now, "provider timestamp");
            Check(quote.ReceivedAtUtc == Now, "retrieval timestamp");
            Check(!quote.IsStale(Now.AddMinutes(2)), "freshness boundary");
            Check(quote.IsStale(Now.AddSeconds(121)), "stale quote detection");
            Check(quote.TimestampUtc.ToOffset(TimeSpan.FromHours(8)).Hour == 15, "China timezone");

            GoldQuote up = GoldQuote.Parse(WithPoint(p =>
            {
                p["new_pri"] = p["valueB"] = 880m;
                p["valueS"] = 871.23m;
            }), Now);
            Check(up.Change == 9.43m && up.ChangePercent == 1.08m, "positive quote");
            Check(up.RedemptionPrice == 871.23m, "spread is not hardcoded to six yuan");
            GoldQuote flat = GoldQuote.Parse(Payload(r => r["lastclo_quo"] = "868.19"), Now);
            Check(flat.Change == 0 && flat.ChangePercent == 0, "unchanged quote");

            var older = Point();
            older["time"] = "2026,10,29,15,16,54";
            string unordered = new JavaScriptSerializer().Serialize(new[] { older, Point(), older });
            Check(GoldQuote.Parse(Payload(r => r["realTimePrice"] = unordered), Now).TimestampUtc == Now,
                "selects newest timestamp rather than last array element");
            GoldQuote olderPair = GoldQuote.Parse(WithPoint(p => p["time"] = "2026,10,29,15,16,54"), Now);
            Check(olderPair.TimestampUtc == Now.AddMinutes(-1), "pair timestamp is not replaced with retrieval time");
            Check(GoldQuote.Parse(WithPoint(p => p["time"] = "2026,10,29,15,7,54"), Now).IsStale(Now),
                "old bank quote remains visibly stale");

            CultureInfo original = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                Check(GoldQuote.Parse(WithPoint(p => p["valueS"] = "862.19"), Now).RedemptionPrice == 862.19m,
                    "locale-independent decimal parsing");
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }

            Reject("{", "malformed JSON");
            Reject("null", "null response");
            Reject("[]", "array response");
            Reject("{\"rc\":0,\"data\":{\"f43\":86440}}", "old Eastmoney payload is not used");
            Reject("<ReferencePriceSettlements/>", "scheduled accumulation XML is not used");
            Reject(Payload(r => r["curr_cod"] = "840"), "wrong summary currency");
            Reject(WithPoint(p => p["CURR_COD"] = "840"), "wrong price-point currency");
            Reject(Payload(r => r.Remove("realTimePrice")), "missing series");
            Reject(Payload(r => r["realTimePrice"] = null), "null series");
            Reject(Payload(r => r["realTimePrice"] = "[]"), "empty series");
            Reject(Payload(r => r["realTimePrice"] = "{}"), "non-array series");
            Reject(Payload(r => r["realTimePrice"] = "[1]"), "invalid series member");
            Reject(Payload(r => r["realTimePrice"] = "bad-json"), "malformed nested series");
            Reject(Payload(r => r.Remove("lastclo_quo")), "missing previous close");
            Reject(Payload(r => r["lastclo_quo"] = "0"), "zero previous close");
            Reject(Payload(r => r["new_pri"] = "-"), "unavailable summary price");
            Reject(Payload(r => r["new_pri"] = "0"), "zero summary price");
            Reject(Payload(r => r["new_pri"] = "999"), "summary/point price mismatch");
            Reject(WithPoint(p => p.Remove("valueB")), "missing buy side");
            Reject(WithPoint(p => p.Remove("valueS")), "missing redemption side");
            Reject(WithPoint(p => p["valueS"] = "-"), "unavailable redemption price");
            Reject(WithPoint(p => p["valueS"] = null), "null redemption price");
            Reject(WithPoint(p => p["valueS"] = 0), "zero redemption price");
            Reject(WithPoint(p => p["valueS"] = -1), "negative redemption price");
            Reject(WithPoint(p => p["valueS"] = 999), "redemption higher than buy price");
            Reject(WithPoint(p => p["new_pri"] = 999), "chart buy side mismatch");
            Reject(Payload(r => r["time"] = "2026-02-30 15:17:54"), "invalid summary date");
            Reject(WithPoint(p => p["time"] = "2026,02,30,15,17,54"), "invalid point date");
            Reject(WithPoint(p => p["time"] = "2026,10,29,15,18,54"), "point later than summary");
            Reject(WithPoint(p => p["time"] = "2026,10,28,15,17,54"), "mismatched quote dates");
            Reject(Payload(r => r["time"] = "2026-10-29 16:17:54"), "future summary");
            Reject(WithPoint(p => p["time"] = "2026,10,29,16,17,54"), "future point");

            Console.WriteLine("PASS: " + assertions + " deterministic quote assertions.");
            if (Array.IndexOf(args, "--live") >= 0)
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                var watch = Stopwatch.StartNew();
                using (var client = new QuoteClient())
                {
                    GoldQuote live = client.GetAsync(CancellationToken.None).GetAwaiter().GetResult();
                    Check(live.BuyPrice > 0 && live.RedemptionPrice > 0, "live buy and redemption prices");
                    Console.WriteLine("LIVE CCB ACTIVE: buy {0:F2}, redemption {1:F2} CNY/g; change {2:+0.00;-0.00;0.00}; {3:+0.00;-0.00;0.00}%; quote {4:yyyy-MM-dd HH:mm:ss zzz}; {5} ms",
                        live.BuyPrice, live.RedemptionPrice, live.Change, live.ChangePercent,
                        live.TimestampUtc.ToOffset(TimeSpan.FromHours(8)), watch.ElapsedMilliseconds);
                }
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex);
            return 1;
        }
    }

    private static Dictionary<string, object> Point()
    {
        return new Dictionary<string, object>
        {
            { "time", "2026,10,29,15,17,54" },
            { "new_pri", 868.19m },
            { "valueB", 868.19m },
            { "valueS", 862.19m },
            { "CURR_COD", "156" }
        };
    }

    private static string Payload(Action<Dictionary<string, object>> change = null)
    {
        var serializer = new JavaScriptSerializer();
        var root = new Dictionary<string, object>
        {
            { "curr_cod", "156" },
            { "new_pri", "868.19" },
            { "lastclo_quo", "870.57" },
            { "time", "2026-10-29 15:17:54" },
            { "realTimePrice", serializer.Serialize(new[] { Point() }) }
        };
        if (change != null)
            change(root);
        return serializer.Serialize(root);
    }

    private static string WithPoint(Action<Dictionary<string, object>> change)
    {
        var point = Point();
        change(point);
        return Payload(root =>
        {
            object buy;
            if (point.TryGetValue("valueB", out buy))
                root["new_pri"] = Convert.ToString(buy, CultureInfo.InvariantCulture);
            root["realTimePrice"] = new JavaScriptSerializer().Serialize(new[] { point });
        });
    }

    private static void Reject(string json, string name)
    {
        try
        {
            GoldQuote.Parse(json, Now);
        }
        catch (InvalidDataException)
        {
            assertions++;
            return;
        }
        throw new InvalidOperationException("Accepted invalid data: " + name);
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException(name);
        assertions++;
    }
}
