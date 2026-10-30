using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using GoldWidget;

internal static class WidgetTests
{
    private static int assertions;

    [STAThread]
    private static int Main()
    {
        try
        {
            Application.EnableVisualStyles();
            var store = new HoldingsStore(Path.Combine(Path.GetTempPath(),
                "GoldWidgetPresentation-" + Guid.NewGuid().ToString("N"), "holdings.json"));
            using (var window = new WidgetWindow(store))
            {
                var price = (Label)window.Controls["GoldPrice"];
                var change = (Label)window.Controls["GoldChange"];
                var status = (Label)window.Controls["QuoteStatus"];
                var profit = (Label)window.Controls["HoldingsProfit"];
                var redemption = (Label)window.Controls["RedemptionPrice"];
                Check(window.ClientSize.Width == 213, "width is two thirds of original 320 pixels");
                Check(window.ClientSize.Height == 262, "bank redemption and profit rows fit expanded height");
                Check(window.Controls["Instrument"].Text.Contains("\u5EFA\u884C"), "header identifies the bank source");
                Check(redemption.Text == "--", "no redemption price before quote arrives");
                Check(profit.Text == "--", "no estimated profit before a quote arrives");
                Check(((Label)window.Controls["HoldingsQuantity"]).Text == "20 \u514B", "default holdings quantity");
                Check(((Label)window.Controls["HoldingsAverage"]).Text == "900.00 \u5143/\u514B", "default average price");
                Check(window.Controls["EditQuantity"] is Button, "quantity pencil button exists");
                Check(window.Controls["EditAverage"] is Button, "average pencil button exists");
                foreach (Control control in window.Controls)
                    Check(control.Left >= 0 && control.Right <= window.ClientSize.Width
                        && control.Top >= 0 && control.Bottom <= window.ClientSize.Height,
                        "control fits compact window: " + control.Name);
                Check(window.TopMost, "initially topmost");
                Check(price.Text == "--.--", "no fabricated initial price");
                Set(window, "lastError", "Test connection failure");
                Update(window);
                Check(status.Text.Contains("\u5931\u8D25"), "first failure is visible");
                Check(price.Text == "--.--", "first failure has no fallback price");

                DateTimeOffset now = DateTimeOffset.UtcNow;
                Set(window, "quote", new GoldQuote(868.19m, 862.19m, -2.38m, -0.27m, now, now));
                Set(window, "lastError", null);
                Update(window);
                Check(price.Text == "868.19 \u2193", "bank buy price and downward arrow");
                Check(redemption.Text == "862.19 \u5143/\u514B", "separate redemption reference price");
                Check(change.Text == "-2.38   -0.27%", "bank price-change output shape");
                Check(price.ForeColor.G > price.ForeColor.R, "downward quote is green");
                Check(profit.Text == "-756.20 \u5143", "loss uses redemption, not buy price");
                Check(profit.ForeColor.G > profit.ForeColor.R, "holding loss is green");
                Check(profit.AccessibleDescription.Contains("\u53E6\u884C\u6536\u53D6"),
                    "profit explicitly excludes separately charged fees");
                Check(status.Text.Contains("5\u79D2\u5237\u65B0"), "default refresh interval");
                Check(price.AccessibilityObject.Name == price.Text, "screen reader receives current price");
                Check(status.Text.Contains(now.ToOffset(TimeSpan.FromHours(8)).ToString("HH:mm:ss")),
                    "displays provider time in China timezone");

                Set(window, "quote", new GoldQuote(870m, 864m, 1.01m, 0.12m, now, now));
                Update(window);
                Check(price.Text == "870.00 \u2191", "upward price and arrow");
                Check(change.Text == "+1.01   +0.12%", "upward changes have plus signs");
                Check(price.ForeColor.R > price.ForeColor.G, "upward quote is red");
                Check(profit.Text == "-720.00 \u5143" && profit.ForeColor.G > profit.ForeColor.R,
                    "daily market rise can still mean a holding loss");

                Set(window, "quote", new GoldQuote(907m, 901m, 1.01m, 0.12m, now, now));
                Update(window);
                Check(profit.Text == "+20.00 \u5143" && profit.ForeColor.R > profit.ForeColor.G,
                    "profitable holding shows positive red amount");
                Set(window, "holdings", new Holdings(1m, 890m));
                Invoke(window, "UpdateHoldingsView");
                Check(profit.Text == "+11.00 \u5143", "holdings edit recalculates without another quote");

                Set(window, "holdings", new Holdings(0.5m, 900m));
                Set(window, "quote", new GoldQuote(906.01m, 900.01m, 1m, 0.1m, now, now));
                Update(window);
                Check(profit.Text == "+0.01 \u5143", "positive midpoint rounded away from zero");
                Set(window, "quote", new GoldQuote(905.99m, 899.99m, -1m, -0.1m, now, now));
                Update(window);
                Check(profit.Text == "-0.01 \u5143", "negative midpoint rounded away from zero");
                Set(window, "holdings", new Holdings(0m, 900m));
                Invoke(window, "UpdateHoldingsView");
                Check(profit.Text == "0.00 \u5143", "zero holdings shows zero profit");
                Set(window, "holdings", new Holdings(20m, 900m));
                Invoke(window, "UpdateHoldingsView");

                Set(window, "quote", new GoldQuote(870m, 864m, 0m, 0m, now, now));
                Update(window);
                Check(price.Text.EndsWith("\u2192"), "flat quote uses sideways arrow");
                Check(change.Text == "0.00   0.00%", "flat quote does not invent signs");

                Set(window, "quote", new GoldQuote(868.19m, 862.19m, -2.38m, -0.27m, now.AddMinutes(-3), now));
                Update(window);
                Check(status.Text.Contains("\u65E7\u62A5\u4EF7"), "stale quote is explicitly marked");
                Check(price.ForeColor == Color.FromArgb(120, 128, 140), "stale price is gray");
                Check(redemption.ForeColor == Color.FromArgb(120, 128, 140), "stale redemption is gray");
                Check(status.AccessibleDescription.Contains("\u4F11\u5E02"), "stale tooltip explains closed markets");
                Check(profit.ForeColor == Color.FromArgb(120, 128, 140), "stale holding profit is gray");
                Check(profit.AccessibleDescription.Contains("\u65E7\u62A5\u4EF7"),
                    "stale estimated profit is explicitly identified");

                Set(window, "quote", new GoldQuote(868.19m, 862.19m, -2.38m, -0.27m, now, now));
                Set(window, "lastError", "Test connection failure");
                Update(window);
                Check(status.Text.Contains("\u66F4\u65B0\u5931\u8D25"), "refresh error is visible");
                Check(price.Text.StartsWith("868.19"), "failed refresh retains last buy price");
                Check(redemption.Text.StartsWith("862.19"), "failed refresh retains last redemption price");
                Check(price.ForeColor == Color.FromArgb(120, 128, 140), "failed refresh dims last price");
                Check(status.AccessibleDescription.Contains("Test connection failure"), "error details are exposed");
                Check(profit.ForeColor == Color.FromArgb(120, 128, 140), "failed refresh dims estimated profit");
                Check(profit.AccessibleDescription.Contains("Test connection failure"), "profit exposes fetch error");

                Invoke(window, "SetRefreshInterval", 30);
                Set(window, "lastError", null);
                Update(window);
                Check(status.Text.Contains("30\u79D2\u5237\u65B0"), "refresh selection takes effect");
                Invoke(window, "TogglePin");
                Check(!window.TopMost, "pin can be disabled");
                Invoke(window, "TogglePin");
                Check(window.TopMost, "pin can be re-enabled");
                Set(window, "holdings", new Holdings(Decimal.MaxValue, 1m));
                Invoke(window, "UpdateHoldingsView");
                Check(profit.Text == "\u8BA1\u7B97\u8D85\u9650", "overflow has explicit visible error");
                Check(profit.ForeColor == Color.Firebrick, "overflow is marked as an error");
            }
            using (var dialog = new HoldingsEditDialog(true, 20m))
            {
                dialog.Show();
                ((TextBox)dialog.Controls["HoldingsInput"]).Text = "-2";
                ((Button)dialog.Controls["SaveHoldings"]).PerformClick();
                Check(dialog.DialogResult != DialogResult.OK, "invalid edit is not accepted");
                Check(dialog.Controls["InputError"].Text.Length > 0, "invalid edit has visible error");
                ((TextBox)dialog.Controls["HoldingsInput"]).Text = "25.125";
                ((Button)dialog.Controls["SaveHoldings"]).PerformClick();
                Check(dialog.DialogResult == DialogResult.OK && dialog.Value == 25.125m,
                    "valid quantity is accepted");
            }
            using (var dialog = new HoldingsEditDialog(false, 900m))
            {
                dialog.Show();
                ((TextBox)dialog.Controls["HoldingsInput"]).Text = "0";
                ((Button)dialog.Controls["SaveHoldings"]).PerformClick();
                Check(dialog.DialogResult != DialogResult.OK, "zero average price is rejected");
                ((TextBox)dialog.Controls["HoldingsInput"]).Text = "901.25";
                ((Button)dialog.Controls["SaveHoldings"]).PerformClick();
                Check(dialog.DialogResult == DialogResult.OK && dialog.Value == 901.25m,
                    "valid average price is accepted");
            }
            Console.WriteLine("PASS: " + assertions + " widget presentation assertions.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex);
            return 1;
        }
    }

    private static void Set(WidgetWindow window, string name, object value)
    {
        typeof(WidgetWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(window, value);
    }

    private static void Update(WidgetWindow window)
    {
        Invoke(window, "UpdateView");
    }

    private static void Invoke(WidgetWindow window, string name, params object[] args)
    {
        typeof(WidgetWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(window, args);
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException(name);
        assertions++;
    }
}
