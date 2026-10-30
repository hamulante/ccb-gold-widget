using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace GoldWidget
{
    public sealed class GoldQuote
    {
        public decimal BuyPrice { get; private set; }
        public decimal RedemptionPrice { get; private set; }
        public decimal Change { get; private set; }
        public decimal ChangePercent { get; private set; }
        public DateTimeOffset TimestampUtc { get; private set; }
        public DateTimeOffset ReceivedAtUtc { get; private set; }

        public GoldQuote(decimal buyPrice, decimal redemptionPrice, decimal change, decimal changePercent,
            DateTimeOffset timestampUtc, DateTimeOffset receivedAtUtc)
        {
            BuyPrice = buyPrice;
            RedemptionPrice = redemptionPrice;
            Change = change;
            ChangePercent = changePercent;
            TimestampUtc = timestampUtc;
            ReceivedAtUtc = receivedAtUtc;
        }

        public bool IsStale(DateTimeOffset now)
        {
            return now - TimestampUtc > TimeSpan.FromMinutes(2);
        }

        public static GoldQuote Parse(string json, DateTimeOffset receivedAtUtc)
        {
            var root = Deserialize(json) as IDictionary<string, object>;
            if (root == null || Text(root, "curr_cod") != "156")
                throw new InvalidDataException("The response is not a CNY active-accumulation quote.");
            DateTimeOffset summaryTime = ChinaTime(Text(root, "time"), false, receivedAtUtc);
            decimal summaryBuy = Number(root, "new_pri");
            decimal previousClose = Number(root, "lastclo_quo");
            if (summaryBuy <= 0 || previousClose <= 0)
                throw new InvalidDataException("The quote and previous close must be positive.");

            var points = Deserialize(Text(root, "realTimePrice")) as object[];
            if (points == null || points.Length == 0)
                throw new InvalidDataException("The active-accumulation price series is empty.");
            IDictionary<string, object> latest = null;
            DateTimeOffset timestamp = DateTimeOffset.MinValue;
            foreach (object raw in points)
            {
                var point = raw as IDictionary<string, object>;
                if (point == null)
                    throw new InvalidDataException("Invalid active-accumulation price point.");
                DateTimeOffset pointTime = ChinaTime(Text(point, "time"), true, receivedAtUtc);
                if (pointTime >= timestamp)
                {
                    latest = point;
                    timestamp = pointTime;
                }
            }

            if (Text(latest, "CURR_COD") != "156")
                throw new InvalidDataException("The active price point is not denominated in CNY.");
            decimal buy = Number(latest, "valueB");
            decimal redemption = Number(latest, "valueS");
            if (buy <= 0 || redemption <= 0 || redemption > buy
                || Number(latest, "new_pri") != buy)
                throw new InvalidDataException("Invalid or inconsistent buy/redemption prices.");
            if (timestamp > summaryTime
                || timestamp.ToOffset(TimeSpan.FromHours(8)).Date != summaryTime.ToOffset(TimeSpan.FromHours(8)).Date
                || (timestamp == summaryTime && buy != summaryBuy))
                throw new InvalidDataException("The price pair does not match the quote summary.");

            try
            {
                decimal change = buy - previousClose;
                decimal percent = Decimal.Round(change / previousClose * 100, 2, MidpointRounding.AwayFromZero);
                return new GoldQuote(buy, redemption, change, percent, timestamp, receivedAtUtc);
            }
            catch (OverflowException ex)
            {
                throw new InvalidDataException("The quote values exceed the supported calculation range.", ex);
            }
        }

        private static object Deserialize(string json)
        {
            try
            {
                return new JavaScriptSerializer().DeserializeObject(json);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidDataException("Invalid CCB quote JSON.", ex);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidDataException("Invalid CCB quote JSON.", ex);
            }
        }

        private static string Text(IDictionary<string, object> values, string name)
        {
            object raw;
            if (values == null || !values.TryGetValue(name, out raw) || !(raw is string)
                || String.IsNullOrWhiteSpace((string)raw))
                throw new InvalidDataException("Missing or invalid CCB quote field: " + name);
            return (string)raw;
        }

        private static DateTimeOffset ChinaTime(string value, bool point, DateTimeOffset receivedAtUtc)
        {
            DateTime date;
            string[] formats = point
                ? new[] { "yyyy,MM,dd,HH,mm,ss", "yyyy,M,d,H,m,s" }
                : new[] { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.fff" };
            if (!DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
                || date.Year < 2000)
                throw new InvalidDataException("Invalid CCB quote timestamp: " + value);
            var timestamp = new DateTimeOffset(date, TimeSpan.FromHours(8)).ToUniversalTime();
            if (timestamp > receivedAtUtc.AddMinutes(5))
                throw new InvalidDataException("The quote timestamp is in the future. Check the system clock.");
            return timestamp;
        }

        private static decimal Number(IDictionary<string, object> values, string name)
        {
            object raw;
            decimal value;
            if (!values.TryGetValue(name, out raw) || raw == null
                || !Decimal.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw new InvalidDataException("Missing or invalid quote field: " + name);
            return value;
        }
    }

    public sealed class QuoteClient : IDisposable
    {
        public const string SourceUrl =
            "https://www.ccb.com/chn/home/gold_new/hqzs/index.shtml";
        public const string ApiUrl =
            "https://www.ccb.com/webtran/static/trendchart/getAccountData.gsp"
            + "?dateType=timeSharing&sec_code=999933";

        private readonly HttpClient client;

        public QuoteClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Referrer = new Uri(SourceUrl);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("GoldWidget/1.0");
            client.DefaultRequestHeaders.CacheControl =
                new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        }

        public async Task<GoldQuote> GetAsync(CancellationToken cancellationToken)
        {
            using (var body = new StringContent(String.Empty, Encoding.UTF8, "application/x-www-form-urlencoded"))
            using (HttpResponseMessage response =
                await client.PostAsync(ApiUrl, body, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return GoldQuote.Parse(json, DateTimeOffset.UtcNow);
            }
        }

        public void Dispose()
        {
            client.Dispose();
        }
    }
}
