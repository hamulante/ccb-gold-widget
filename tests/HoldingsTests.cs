using System;
using System.Globalization;
using System.IO;
using System.Threading;
using GoldWidget;

internal static class HoldingsTests
{
    private static int assertions;

    private static int Main()
    {
        string directory = Path.Combine(Path.GetTempPath(), "GoldWidgetHoldings-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "holdings.json");
        try
        {
            var store = new HoldingsStore(path);
            Holdings initial = store.Load();
            Check(initial.Quantity == 20m && initial.AveragePrice == 900m, "initial defaults");
            Check(initial.CalculateProfitLoss(864.60m) == -708m, "exact unrounded loss");
            Check(initial.CalculateProfitLoss(901m) == 20m, "positive holding profit");
            Check(initial.CalculateProfitLoss(900m) == 0m, "break-even price");
            Check(new Holdings(0m, 900m).CalculateProfitLoss(864.60m) == 0m, "empty position has no profit");
            Check(new Holdings(0.001m, 900m).CalculateProfitLoss(900.01m) == 0.00001m,
                "fractional quantity retains arithmetic precision");
            bool invalidPrice = false;
            try { initial.CalculateProfitLoss(0m); }
            catch (ArgumentOutOfRangeException) { invalidPrice = true; }
            Check(invalidPrice, "invalid market price is not used");
            bool overflow = false;
            try { new Holdings(Decimal.MaxValue, 1m).CalculateProfitLoss(3m); }
            catch (OverflowException) { overflow = true; }
            Check(overflow, "overflow does not return a fabricated profit");
            Check(!Directory.Exists(directory), "reading defaults does not create files");
            store.Save(new Holdings(25.125m, 901.25m));
            Holdings persisted = new HoldingsStore(path).Load();
            Check(persisted.Quantity == 25.125m && persisted.AveragePrice == 901.25m, "restart persistence");
            store.Save(new Holdings(0m, 900m));
            Check(store.Load().Quantity == 0m, "existing file replaced with zero quantity");
            Check(Directory.GetFiles(directory).Length == 1, "no temporary files after save");

            decimal value;
            Check(Holdings.TryParseInput("20", true, out value) && value == 20m, "quantity input");
            Check(Holdings.TryParseInput("900", false, out value) && value == 900m, "average input");
            Check(Holdings.TryParseInput(" 0 ", true, out value) && value == 0m, "empty holdings allowed");
            foreach (string invalid in new[] { "", "abc", "-1", "1,000", "NaN", "1e10", "1.2.3" })
                Check(!Holdings.TryParseInput(invalid, true, out value), "invalid quantity: " + invalid);
            Check(!Holdings.TryParseInput("0", false, out value), "zero average rejected");
            Check(!Holdings.TryParseInput("-1", false, out value), "negative average rejected");

            CultureInfo original = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                Check(Holdings.TryParseInput("20", true, out value) && value == 20m, "input independent of locale");
                store.Save(new Holdings(20.012345m, 900.0075m));
                Check(store.Load().Quantity == 20.012345m && store.Load().AveragePrice == 900.0075m,
                    "decimal precision preserved across save");
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }

            foreach (string invalidJson in new[]
            {
                "{", "null", "{\"quantity\":20}",
                "{\"quantity\":-1,\"averagePrice\":900}",
                "{\"quantity\":20,\"averagePrice\":0}",
                "{\"quantity\":\"-\",\"averagePrice\":900}"
            })
            {
                File.WriteAllText(path, invalidJson);
                bool rejected = false;
                try { store.Load(); }
                catch (InvalidDataException) { rejected = true; }
                Check(rejected, "corrupt file is not silently replaced");
                Check(File.ReadAllText(path) == invalidJson, "corrupt file left untouched");
            }
            Console.WriteLine("PASS: " + assertions + " holdings validation/persistence assertions.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex);
            return 1;
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
            if (Directory.Exists(directory))
                Directory.Delete(directory);
        }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException(name);
        assertions++;
    }
}
