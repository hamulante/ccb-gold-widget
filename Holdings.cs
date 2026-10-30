using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace GoldWidget
{
    public sealed class Holdings
    {
        public decimal Quantity { get; private set; }
        public decimal AveragePrice { get; private set; }

        public Holdings(decimal quantity, decimal averagePrice)
        {
            if (quantity < 0)
                throw new ArgumentOutOfRangeException("quantity", "Quantity cannot be negative.");
            if (averagePrice <= 0)
                throw new ArgumentOutOfRangeException("averagePrice", "Average price must be positive.");
            Quantity = quantity;
            AveragePrice = averagePrice;
        }

        public decimal CalculateProfitLoss(decimal marketPrice)
        {
            if (marketPrice <= 0)
                throw new ArgumentOutOfRangeException("marketPrice", "Market price must be positive.");
            return (marketPrice - AveragePrice) * Quantity;
        }

        public static bool TryParseInput(string text, bool isQuantity, out decimal value)
        {
            return Decimal.TryParse(text,
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
                CultureInfo.InvariantCulture, out value)
                && (isQuantity ? value >= 0 : value > 0);
        }
    }

    public sealed class HoldingsStore
    {
        private readonly string path;

        public static string DefaultPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GoldWidget", "holdings.json");
            }
        }

        public HoldingsStore(string path)
        {
            this.path = Path.GetFullPath(path);
        }

        public Holdings Load()
        {
            if (!File.Exists(path))
                return new Holdings(20m, 900m);

            IDictionary<string, object> values;
            try
            {
                values = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path, Encoding.UTF8))
                    as IDictionary<string, object>;
            }
            catch (ArgumentException ex)
            {
                throw new InvalidDataException("Invalid holdings file: " + path, ex);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidDataException("Invalid holdings file: " + path, ex);
            }

            decimal quantity = ReadDecimal(values, "quantity");
            decimal average = ReadDecimal(values, "averagePrice");
            if (quantity < 0 || average <= 0)
                throw new InvalidDataException("Invalid holdings values in: " + path);
            return new Holdings(quantity, average);
        }

        public void Save(Holdings holdings)
        {
            if (holdings == null)
                throw new ArgumentNullException("holdings");

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                string json = new JavaScriptSerializer().Serialize(new Dictionary<string, object>
                {
                    { "quantity", holdings.Quantity },
                    { "averagePrice", holdings.AveragePrice }
                });
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(json);
                    writer.Flush();
                    stream.Flush(true);
                }
                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        private static decimal ReadDecimal(IDictionary<string, object> values, string name)
        {
            object raw;
            decimal result;
            if (values == null || !values.TryGetValue(name, out raw) || raw == null
                || !Decimal.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out result))
                throw new InvalidDataException("Missing or invalid holdings field: " + name);
            return result;
        }
    }
}
