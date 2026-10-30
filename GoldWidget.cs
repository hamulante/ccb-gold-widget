using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GoldWidget
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new WidgetWindow());
            }
            catch (IOException ex)
            {
                ShowSettingsError(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                ShowSettingsError(ex.Message);
            }
        }

        private static void ShowSettingsError(string details)
        {
            MessageBox.Show("\u65E0\u6CD5\u8BFB\u53D6\u6301\u4ED3\u8BBE\u7F6E\uFF0C\u7A0B\u5E8F\u672A\u542F\u52A8\u3002"
                + "\n" + HoldingsStore.DefaultPath + "\n\n" + details,
                "\u6301\u4ED3\u8BBE\u7F6E\u9519\u8BEF", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public sealed class WidgetWindow : Form
    {
        private static readonly Color Blue = Color.FromArgb(49, 91, 166);
        private static readonly Color Up = Color.FromArgb(213, 37, 46);
        private static readonly Color Down = Color.FromArgb(0, 157, 36);
        private static readonly Color Muted = Color.FromArgb(120, 128, 140);
        private readonly Label priceLabel;
        private readonly Label changeLabel;
        private readonly Label statusLabel;
        private readonly Button refreshButton;
        private readonly Button pinButton;
        private readonly Label quantityLabel;
        private readonly Label averageLabel;
        private readonly Label profitLabel;
        private readonly Label redemptionLabel;
        private readonly HoldingsStore holdingsStore;
        private Holdings holdings;
        private readonly ToolTip toolTip = new ToolTip();
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly ToolStripMenuItem pinItem;
        private readonly ToolStripMenuItem intervalItem;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly QuoteClient client = new QuoteClient();
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private GoldQuote quote;
        private string lastError;
        private bool refreshing;
        private bool closing;
        private int refreshSeconds = 5;
        private DateTimeOffset nextRefresh = DateTimeOffset.MinValue;

        public WidgetWindow() : this(new HoldingsStore(HoldingsStore.DefaultPath))
        {
        }

        internal WidgetWindow(HoldingsStore store)
        {
            holdingsStore = store;
            holdings = holdingsStore.Load();
            Text = "GoldWidget - CCB Active Gold";
            AccessibleName = "CCB Active Gold Widget";
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(213, 262);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9);
            TopMost = true;
            ShowInTaskbar = false;

            var title = MakeLabel("Instrument", "\u5EFA\u884C\u4E70\u5165\u00B7\u5143/\u514B",
                new Rectangle(8, 8, 99, 21), 7.5f, FontStyle.Regular);
            title.ForeColor = Muted;
            title.TextAlign = ContentAlignment.MiddleLeft;
            refreshButton = MakeButton("Refresh", "\u21BB", 108, 25);
            pinButton = MakeButton("Pin", "\u7F6E\u9876", 133, 44);
            var closeButton = MakeButton("Close", "\u00D7", 181, 24);
            pinButton.ForeColor = Blue;
            refreshButton.Click += async (sender, args) => await RefreshQuoteAsync();
            pinButton.Click += (sender, args) => TogglePin();
            closeButton.Click += (sender, args) => Close();
            toolTip.SetToolTip(refreshButton, "\u7ACB\u5373\u5237\u65B0");
            toolTip.SetToolTip(pinButton, "\u5207\u6362\u7A97\u53E3\u7F6E\u9876");
            toolTip.SetToolTip(closeButton, "\u9000\u51FA");
            refreshButton.AccessibleName = "\u7ACB\u5373\u5237\u65B0";
            pinButton.AccessibleName = "\u5207\u6362\u7F6E\u9876";
            closeButton.AccessibleName = "\u9000\u51FA";

            priceLabel = MakeLabel("GoldPrice", "--.--",
                new Rectangle(4, 31, 205, 48), 28, FontStyle.Bold);
            changeLabel = MakeLabel("GoldChange", "\u6DA8\u8DCC\u989D   \u6DA8\u8DCC\u5E45",
                new Rectangle(4, 80, 205, 25), 12, FontStyle.Regular);
            statusLabel = MakeLabel("QuoteStatus", "\u6B63\u5728\u8FDE\u63A5\u884C\u60C5\u2026",
                new Rectangle(4, 108, 205, 18), 7.5f, FontStyle.Regular);
            priceLabel.ForeColor = changeLabel.ForeColor = statusLabel.ForeColor = Muted;

            var redemptionTitle = MakeLabel("RedemptionTitle", "\u53C2\u8003\u8D4E\u56DE",
                new Rectangle(10, 141, 64, 23), 9, FontStyle.Regular);
            redemptionTitle.ForeColor = Muted;
            redemptionTitle.TextAlign = ContentAlignment.MiddleLeft;
            redemptionLabel = MakeLabel("RedemptionPrice", "--",
                new Rectangle(74, 141, 129, 23), 10, FontStyle.Bold);
            redemptionLabel.TextAlign = ContentAlignment.MiddleRight;
            redemptionLabel.AutoEllipsis = true;
            redemptionLabel.ForeColor = Muted;
            var quantityTitle = MakeLabel("QuantityTitle", "\u6301\u4ED3",
                new Rectangle(10, 171, 42, 23), 9, FontStyle.Regular);
            var averageTitle = MakeLabel("AverageTitle", "\u5747\u4EF7",
                new Rectangle(10, 201, 42, 23), 9, FontStyle.Regular);
            quantityTitle.ForeColor = averageTitle.ForeColor = Muted;
            quantityTitle.TextAlign = averageTitle.TextAlign = ContentAlignment.MiddleLeft;
            quantityLabel = MakeLabel("HoldingsQuantity", "",
                new Rectangle(52, 171, 126, 23), 10, FontStyle.Regular);
            averageLabel = MakeLabel("HoldingsAverage", "",
                new Rectangle(52, 201, 126, 23), 10, FontStyle.Regular);
            quantityLabel.TextAlign = averageLabel.TextAlign = ContentAlignment.MiddleRight;
            quantityLabel.AutoEllipsis = averageLabel.AutoEllipsis = true;
            var profitTitle = MakeLabel("ProfitTitle", "\u6301\u4ED3\u76C8\u4E8F",
                new Rectangle(10, 231, 64, 23), 9, FontStyle.Regular);
            profitTitle.ForeColor = Muted;
            profitTitle.TextAlign = ContentAlignment.MiddleLeft;
            profitLabel = MakeLabel("HoldingsProfit", "--",
                new Rectangle(74, 231, 129, 23), 10, FontStyle.Bold);
            profitLabel.TextAlign = ContentAlignment.MiddleRight;
            profitLabel.AutoEllipsis = true;
            var editQuantity = MakeButton("EditQuantity", "\u270E", 181, 24);
            var editAverage = MakeButton("EditAverage", "\u270E", 181, 24);
            editQuantity.Top = 171;
            editAverage.Top = 201;
            editQuantity.Font = editAverage.Font = new Font("Segoe UI Symbol", 12);
            editQuantity.AccessibleName = "\u4FEE\u6539\u6301\u4ED3\u6570\u91CF";
            editAverage.AccessibleName = "\u4FEE\u6539\u6301\u4ED3\u5747\u4EF7";
            toolTip.SetToolTip(editQuantity, editQuantity.AccessibleName);
            toolTip.SetToolTip(editAverage, editAverage.AccessibleName);
            editQuantity.Click += (sender, args) => EditHoldings(true);
            editAverage.Click += (sender, args) => EditHoldings(false);
            UpdateHoldingsView();

            var refreshItem = menu.Items.Add("\u7ACB\u5373\u5237\u65B0");
            refreshItem.Click += async (sender, args) => await RefreshQuoteAsync();
            pinItem = new ToolStripMenuItem("\u7A97\u53E3\u7F6E\u9876") { Checked = true };
            pinItem.Click += (sender, args) => TogglePin();
            menu.Items.Add(pinItem);
            intervalItem = new ToolStripMenuItem("\u5237\u65B0\u95F4\u9694");
            foreach (int seconds in new[] { 5, 10, 30 })
            {
                var item = new ToolStripMenuItem(seconds + " \u79D2")
                {
                    Tag = seconds,
                    Checked = seconds == refreshSeconds
                };
                item.Click += (sender, args) => SetRefreshInterval((int)((ToolStripMenuItem)sender).Tag);
                intervalItem.DropDownItems.Add(item);
            }
            menu.Items.Add(intervalItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("\u6253\u5F00\u5EFA\u884C\u5B98\u65B9\u884C\u60C5", null,
                (sender, args) => OpenSource());
            menu.Items.Add("\u9000\u51FA", null, (sender, args) => Close());
            ContextMenuStrip = menu;

            foreach (Control control in new Control[]
                { this, title, priceLabel, changeLabel, statusLabel,
                    quantityTitle, averageTitle, quantityLabel, averageLabel, profitTitle, profitLabel,
                    redemptionTitle, redemptionLabel })
            {
                control.ContextMenuStrip = menu;
                control.MouseDown += DragWindow;
            }

            timer.Interval = 1000;
            timer.Tick += async (sender, args) =>
            {
                UpdateView();
                if (!refreshing && DateTimeOffset.UtcNow >= nextRefresh)
                    await RefreshQuoteAsync();
            };
            Shown += async (sender, args) =>
            {
                Rectangle area = Screen.FromControl(this).WorkingArea;
                Location = new Point(Math.Max(area.Left, area.Right - Width - 24), area.Top + 48);
                timer.Start();
                await RefreshQuoteAsync();
            };
            FormClosing += (sender, args) =>
            {
                closing = true;
                timer.Stop();
                cancellation.Cancel();
            };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ClassStyle |= 0x20000;
                return parameters;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var border = new Pen(Color.FromArgb(217, 227, 241)))
                e.Graphics.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            using (var top = new Pen(Blue, 3))
                e.Graphics.DrawLine(top, 0, 1, ClientSize.Width, 1);
            using (var separator = new Pen(Color.FromArgb(235, 239, 244)))
                e.Graphics.DrawLine(separator, 10, 132, ClientSize.Width - 10, 132);
        }

        private Label MakeLabel(string name, string text, Rectangle bounds, float size, FontStyle style)
        {
            var label = new Label
            {
                Name = name,
                Text = text,
                Bounds = bounds,
                Font = new Font("Segoe UI", size, style),
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false
            };
            Controls.Add(label);
            return label;
        }

        private Button MakeButton(string name, string text, int left, int width)
        {
            var button = new Button
            {
                Name = name,
                AccessibleName = name,
                Text = text,
                Bounds = new Rectangle(left, 7, width, 23),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Muted,
                BackColor = Color.White,
                TabStop = false,
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderSize = 0;
            Controls.Add(button);
            return button;
        }

        private async Task RefreshQuoteAsync()
        {
            if (refreshing || closing)
                return;
            refreshing = true;
            refreshButton.Enabled = false;
            try
            {
                GoldQuote result = await client.GetAsync(cancellation.Token);
                if (!closing)
                {
                    quote = result;
                    lastError = null;
                }
            }
            catch (HttpRequestException ex)
            {
                if (!closing)
                    lastError = "\u7F51\u7EDC\u6216\u884C\u60C5\u670D\u52A1\u9519\u8BEF: " + ex.Message;
            }
            catch (TaskCanceledException)
            {
                if (!closing)
                    lastError = "\u8BF7\u6C42\u8D85\u65F6\uFF0810\u79D2\uFF09";
            }
            catch (InvalidDataException ex)
            {
                if (!closing)
                    lastError = "\u884C\u60C5\u6570\u636E\u65E0\u6548: " + ex.Message;
            }
            finally
            {
                refreshing = false;
                nextRefresh = DateTimeOffset.UtcNow.AddSeconds(refreshSeconds);
                if (!closing)
                {
                    refreshButton.Enabled = true;
                    UpdateView();
                }
            }
        }

        private void UpdateView()
        {
            if (closing)
                return;

            bool stale = quote != null && quote.IsStale(DateTimeOffset.UtcNow);
            bool error = lastError != null;
            if (quote != null)
            {
                string arrow = quote.Change > 0 ? "\u2191" : quote.Change < 0 ? "\u2193" : "\u2192";
                priceLabel.Text = quote.BuyPrice.ToString("F2", CultureInfo.InvariantCulture) + " " + arrow;
                redemptionLabel.Text = quote.RedemptionPrice.ToString("F2", CultureInfo.InvariantCulture) + " \u5143/\u514B";
                redemptionLabel.ForeColor = stale || error ? Muted : Color.FromArgb(65, 75, 90);
                changeLabel.Text = quote.Change.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture)
                    + "   " + quote.ChangePercent.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + "%";
                Color color = stale || error ? Muted : quote.Change > 0 ? Up : quote.Change < 0 ? Down : Muted;
                priceLabel.ForeColor = changeLabel.ForeColor = color;

                DateTimeOffset chinaTime = quote.TimestampUtc.ToOffset(TimeSpan.FromHours(8));
                string time = chinaTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                statusLabel.Text = error
                    ? "\u66F4\u65B0\u5931\u8D25 \u00B7 \u65E7\u62A5\u4EF7 " + time
                    : (stale ? "\u65E7\u62A5\u4EF7 " : "\u62A5\u4EF7 ") + time
                        + " \u00B7 " + refreshSeconds + "\u79D2\u5237\u65B0";
                string details = "\u5EFA\u8BBE\u94F6\u884C / \u9EC4\u91D1\u4E3B\u52A8\u79EF\u5B58 / \u5143\u6BCF\u514B"
                    + "\n\u5BA2\u6237\u4E70\u5165\u53C2\u8003\u4EF7: " + quote.BuyPrice.ToString(CultureInfo.InvariantCulture)
                    + "\n\u53C2\u8003\u8D4E\u56DE\u4EF7: " + quote.RedemptionPrice.ToString(CultureInfo.InvariantCulture)
                    + "\n\u884C\u60C5\u65F6\u95F4\uFF08\u5317\u4EAC\u65F6\u95F4\uFF09: "
                    + chinaTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                    + "\n\u6700\u8FD1\u53D6\u6570: "
                    + quote.ReceivedAtUtc.ToOffset(TimeSpan.FromHours(8))
                        .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                    + "\n\u6DA8\u8DCC\u76F8\u5BF9\u5EFA\u884C\u4E0A\u6B21\u6536\u76D8\u4E70\u5165\u4EF7\uFF1B\u6700\u7EC8\u6210\u4EA4\u4EE5 App \u62A5\u4EF7\u4E3A\u51C6\u3002";
                if (stale)
                    details += "\n\u62A5\u4EF7\u5DF2\u8D85\u8FC72\u5206\u949F\uFF0C\u53EF\u80FD\u4F11\u5E02\u6216\u6570\u636E\u5EF6\u8FDF\u3002";
                if (error)
                    details += "\n" + lastError;
                SetDetails(details);
            }
            else
            {
                statusLabel.Text = error
                    ? "\u83B7\u53D6\u5931\u8D25 \u00B7 \u5C06\u81EA\u52A8\u91CD\u8BD5"
                    : "\u6B63\u5728\u8FDE\u63A5\u884C\u60C5\u2026";
                SetDetails(error ? lastError : "\u6B63\u5728\u8BF7\u6C42\u5EFA\u884C\u4E3B\u52A8\u79EF\u5B58\u884C\u60C5");
            }
            statusLabel.ForeColor = error ? Color.Firebrick : stale ? Color.DarkGoldenrod : Muted;
            UpdateProfitLossView();
        }

        private void SetDetails(string text)
        {
            foreach (Control control in new Control[] { priceLabel, changeLabel, statusLabel, redemptionLabel })
            {
                toolTip.SetToolTip(control, text);
                control.AccessibleDescription = text;
            }
        }

        private void TogglePin()
        {
            TopMost = !TopMost;
            pinItem.Checked = TopMost;
            pinButton.ForeColor = TopMost ? Blue : Muted;
            toolTip.SetToolTip(pinButton, TopMost
                ? "\u5DF2\u7F6E\u9876\uFF0C\u70B9\u51FB\u53D6\u6D88"
                : "\u672A\u7F6E\u9876\uFF0C\u70B9\u51FB\u5F00\u542F");
        }

        private void UpdateHoldingsView()
        {
            quantityLabel.Text = holdings.Quantity.ToString("0.############################",
                CultureInfo.InvariantCulture) + " \u514B";
            averageLabel.Text = holdings.AveragePrice.ToString("0.00##########################",
                CultureInfo.InvariantCulture) + " \u5143/\u514B";
            toolTip.SetToolTip(quantityLabel, "\u6301\u4ED3\u6570\u91CF: " + quantityLabel.Text);
            toolTip.SetToolTip(averageLabel, "\u6301\u4ED3\u5747\u4EF7: " + averageLabel.Text
                + "\n\u624B\u52A8\u8F93\u5165\uFF0C\u4E0D\u542B\u81EA\u52A8\u624B\u7EED\u8D39\u6838\u7B97\u3002");
            UpdateProfitLossView();
        }

        private void UpdateProfitLossView()
        {
            string details = "\u4F30\u7B97\u76C8\u4E8F = (\u5EFA\u884C\u53C2\u8003\u8D4E\u56DE\u4EF7 - \u6301\u4ED3\u5747\u4EF7) \u00D7 \u6301\u4ED3\u514B\u6570"
                + "\n\u5DF2\u7528\u8D4E\u56DE\u4FA7\u4EF7\u683C\uFF0C\u4E0D\u4EE3\u8868\u6700\u7EC8\u6210\u4EA4\uFF0C\u4E0D\u542B\u53E6\u884C\u6536\u53D6\u7684\u624B\u7EED\u8D39\u3002";
            if (quote == null)
            {
                profitLabel.Text = "--";
                profitLabel.ForeColor = Muted;
                details += "\n\u5C1A\u65E0\u884C\u60C5\uFF0C\u65E0\u6CD5\u4F30\u7B97\u3002";
            }
            else
            {
                try
                {
                    decimal profit = Decimal.Round(holdings.CalculateProfitLoss(quote.RedemptionPrice),
                        2, MidpointRounding.AwayFromZero);
                    profitLabel.Text = profit.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + " \u5143";
                    bool old = lastError != null || quote.IsStale(DateTimeOffset.UtcNow);
                    profitLabel.ForeColor = old ? Muted : profit > 0 ? Up : profit < 0 ? Down : Muted;
                    details += "\n(" + quote.RedemptionPrice.ToString(CultureInfo.InvariantCulture)
                        + " - " + holdings.AveragePrice.ToString(CultureInfo.InvariantCulture)
                        + ") \u00D7 " + holdings.Quantity.ToString(CultureInfo.InvariantCulture)
                        + " = " + profitLabel.Text
                        + "\n\u62A5\u4EF7\u65F6\u95F4\uFF08\u5317\u4EAC\u65F6\u95F4\uFF09: "
                        + quote.TimestampUtc.ToOffset(TimeSpan.FromHours(8))
                            .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    if (old)
                        details += "\n\u57FA\u4E8E\u65E7\u62A5\u4EF7\u4F30\u7B97\uFF0C\u8BF7\u52FF\u5F53\u4F5C\u5F53\u524D\u53EF\u6210\u4EA4\u76C8\u4E8F\u3002";
                }
                catch (OverflowException ex)
                {
                    profitLabel.Text = "\u8BA1\u7B97\u8D85\u9650";
                    profitLabel.ForeColor = Color.Firebrick;
                    details += "\n\u6570\u503C\u8D85\u51FA\u8BA1\u7B97\u8303\u56F4\uFF0C\u8BF7\u68C0\u67E5\u6301\u4ED3\u8F93\u5165\u3002\n" + ex.Message;
                }
            }
            if (lastError != null)
                details += "\n" + lastError;
            toolTip.SetToolTip(profitLabel, details);
            profitLabel.AccessibleDescription = details;
        }

        private void EditHoldings(bool quantity)
        {
            using (var dialog = new HoldingsEditDialog(quantity,
                quantity ? holdings.Quantity : holdings.AveragePrice))
            {
                dialog.TopMost = TopMost;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                var edited = new Holdings(quantity ? dialog.Value : holdings.Quantity,
                    quantity ? holdings.AveragePrice : dialog.Value);
                try
                {
                    holdingsStore.Save(edited);
                    holdings = edited;
                    UpdateHoldingsView();
                }
                catch (IOException ex)
                {
                    ShowSaveError(ex.Message);
                }
                catch (UnauthorizedAccessException ex)
                {
                    ShowSaveError(ex.Message);
                }
            }
        }

        private void ShowSaveError(string details)
        {
            MessageBox.Show(this, "\u4FDD\u5B58\u5931\u8D25\uFF0C\u6301\u4ED3\u6570\u636E\u672A\u4FEE\u6539\u3002\n\n" + details,
                "\u65E0\u6CD5\u4FDD\u5B58\u6301\u4ED3", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void SetRefreshInterval(int seconds)
        {
            refreshSeconds = seconds;
            nextRefresh = DateTimeOffset.UtcNow.AddSeconds(seconds);
            foreach (ToolStripMenuItem item in intervalItem.DropDownItems)
                item.Checked = (int)item.Tag == seconds;
            UpdateView();
        }

        private void OpenSource()
        {
            try
            {
                Process.Start(new ProcessStartInfo(QuoteClient.SourceUrl) { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                MessageBox.Show(this, ex.Message, "\u65E0\u6CD5\u6253\u5F00\u884C\u60C5\u6765\u6E90",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                cancellation.Dispose();
                client.Dispose();
                toolTip.Dispose();
                menu.Dispose();
            }
            base.Dispose(disposing);
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    }

    internal sealed class HoldingsEditDialog : Form
    {
        internal decimal Value { get; private set; }

        internal HoldingsEditDialog(bool quantity, decimal current)
        {
            Text = quantity ? "\u4FEE\u6539\u6301\u4ED3\u6570\u91CF" : "\u4FEE\u6539\u6301\u4ED3\u5747\u4EF7";
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(276, 144);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MaximizeBox = MinimizeBox = false;
            Font = new Font("Segoe UI", 9);
            var prompt = new Label
            {
                Text = quantity ? "\u6301\u4ED3\u6570\u91CF\uFF08\u514B\uFF0C\u53EF\u4E3A0\uFF09"
                    : "\u6301\u4ED3\u5747\u4EF7\uFF08\u5143/\u514B\uFF0C\u5927\u4E8E0\uFF09",
                Bounds = new Rectangle(12, 12, 252, 22)
            };
            var input = new TextBox
            {
                Name = "HoldingsInput",
                Text = current.ToString(CultureInfo.InvariantCulture),
                Bounds = new Rectangle(12, 38, 252, 24),
                TabIndex = 0,
                AccessibleName = prompt.Text
            };
            var error = new Label
            {
                Name = "InputError",
                ForeColor = Color.Firebrick,
                Bounds = new Rectangle(12, 68, 252, 28)
            };
            var save = new Button
            {
                Text = "\u4FDD\u5B58",
                Name = "SaveHoldings",
                Bounds = new Rectangle(102, 106, 76, 26),
                TabIndex = 1
            };
            var cancel = new Button
            {
                Text = "\u53D6\u6D88",
                Bounds = new Rectangle(188, 106, 76, 26),
                DialogResult = DialogResult.Cancel,
                TabIndex = 2
            };
            save.Click += (sender, args) =>
            {
                decimal parsed;
                if (!Holdings.TryParseInput(input.Text, quantity, out parsed))
                {
                    error.Text = quantity ? "\u8BF7\u8F93\u5165\u4E0D\u5C0F\u4E8E0\u7684\u6570\u5B57"
                        : "\u8BF7\u8F93\u5165\u5927\u4E8E0\u7684\u6570\u5B57";
                    input.Focus();
                    input.SelectAll();
                    return;
                }
                Value = parsed;
                DialogResult = DialogResult.OK;
            };
            Controls.AddRange(new Control[] { prompt, input, error, save, cancel });
            AcceptButton = save;
            CancelButton = cancel;
            Shown += (sender, args) => { input.Focus(); input.SelectAll(); };
        }
    }
}
