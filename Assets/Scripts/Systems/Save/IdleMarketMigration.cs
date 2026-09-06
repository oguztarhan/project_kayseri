using System;
using System.Collections.Generic;

namespace Game.Systems
{
    /// <summary>Detached row conversion: does not touch live saves, wallets or the global reset version.</summary>
    public static class IdleMarketMigration
    {
        public const int SchemaVersion = 1;

        public static IdleMarketYard Convert(MarketYard legacy, string productId, IdleMarketYard existing = null)
        {
            if (legacy == null || string.IsNullOrWhiteSpace(legacy.id)) throw new ArgumentException("Legacy island ID required.");
            if (existing != null)
            {
                if (existing.id != legacy.id) throw new ArgumentException("Existing row belongs to another island.");
                Validate(existing);
                return existing; // Never re-credit legacy stock, including after the new row sold out.
            }
            if (string.IsNullOrWhiteSpace(productId)) throw new ArgumentException("Explicit legacy product ID required.");
            var result = new IdleMarketYard
            {
                schemaVersion = SchemaVersion, id = legacy.id,
                depositSlots = legacy.depositSlots, queueSlots = legacy.queueSlots,
                hireCarry = legacy.hireCarry, hireServe = legacy.hireServe, dispatchLevel = legacy.hireCollect,
                products = new List<MarketProductStock>
                {
                    new MarketProductStock { productId = productId, stock = legacy.stock, deliveredPerMin = legacy.deliveredPerMin }
                }
            };
            Validate(result);
            return result;
        }

        public static void Validate(IdleMarketYard row)
        {
            if (row == null || row.schemaVersion != SchemaVersion || string.IsNullOrWhiteSpace(row.id) || row.products == null)
                throw new ArgumentException("Unsupported or incomplete idle market row; preserve the source save.");
            if (row.depositSlots < 1 || row.queueSlots < 1 || row.hireCarry < 0 || row.hireServe < 0 || row.dispatchLevel < 0)
                throw new ArgumentException("Invalid market progression; preserve the source save.");
            var ids = new HashSet<string>();
            foreach (var product in row.products)
                if (product == null || string.IsNullOrWhiteSpace(product.productId) || !ids.Add(product.productId) ||
                    !Quantity(product.stock) || !Quantity(product.voyageReserved) || !Quantity(product.deliveredPerMin))
                    throw new ArgumentException("Invalid or duplicate product stock; preserve the source save.");
        }

        private static bool Quantity(double value) => value >= 0 && !double.IsInfinity(value) && !double.IsNaN(value);
    }
}
