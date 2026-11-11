using System.IO;
using System.Text.Json;

namespace FullAIon2Companion;

public sealed record CraftQuoteLine(int Item, string Name, string? Icon, long PerCraft, long Required, long Owned, long Used,
    long Missing, bool Bound, long? UnitPrice, long? OwnedValue, long? BuyCost, long? TotalValue);

public sealed record CraftQuote(int Recipe, int ResultItem, string ResultName, string? ResultIcon, long Requested, long Crafts,
    long Produced, int Server, bool InventoryComplete, DateTime FetchedAt, DateTime? MarketSnapshotAt,
    IReadOnlyList<CraftQuoteLine> Materials, long? OwnedValue, long? BuyCost, long? TotalValue);

/// <summary>Prices the materials for an output quantity against a freshly fetched server Market board.</summary>
public static class CraftCalculator
{
    sealed record Recipe(int Id, int Result, long ResultCount, IReadOnlyList<(int Item, long Count)> Materials);
    static readonly Dictionary<int, Recipe> recipes = Load();

    static Dictionary<int, Recipe> Load()
    {
        var found = new Dictionary<int, Recipe>();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "recipes.json")));
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                if (!entry.TryGetProperty("materials", out var materials) || materials.GetArrayLength() == 0) continue;
                int id = entry.GetProperty("id").GetInt32();
                var result = entry.GetProperty("result");
                long output = result.GetProperty("count").GetInt64();
                if (output <= 0) continue;
                var parts = materials.EnumerateArray().Select(m => (Item: m.GetProperty("id").GetInt32(), Count: m.GetProperty("count").GetInt64()))
                    .Where(m => m.Item > 0 && m.Count > 0).ToList();
                if (parts.Count > 0) found[id] = new(id, result.GetProperty("id").GetInt32(), output, parts);
            }
        }
        catch { }
        return found;
    }

    public static CraftQuote Quote(int recipeId, int requested, int server, InventoryWatch inventory, MarketBoard board)
    {
        if (!recipes.TryGetValue(recipeId, out var recipe)) throw new InvalidOperationException(L.T("Receta sin materiales conocidos."));
        if (requested is < 1 or > 100_000) throw new InvalidOperationException(L.T("Elige una cantidad entre 1 y 100.000."));
        if (server <= 0 || board.Server != server) throw new InvalidOperationException(L.T("Selecciona un servidor del Mercado."));

        long crafts = (requested + recipe.ResultCount - 1) / recipe.ResultCount;
        long produced = checked(crafts * recipe.ResultCount);
        var held = inventory.Holdings(_ => true);
        var prices = board.Rows.GroupBy(r => r.Item).ToDictionary(g => g.Key, g => g.First());
        var materials = recipe.Materials.GroupBy(m => m.Item).Select(g => (Item: g.Key, PerCraft: g.Sum(m => m.Count))).ToList();
        var lines = new List<CraftQuoteLine>(materials.Count);
        foreach (var material in materials)
        {
            long needed = checked(material.PerCraft * crafts);
            long owned = held.GetValueOrDefault(material.Item);
            long used = Math.Min(needed, owned), missing = needed - used;
            var info = InventoryWatch.Info(material.Item);
            bool bound = InventoryWatch.Bound(material.Item);
            long? price = !bound && prices.GetValueOrDefault(material.Item)?.Price is long active and > 0 ? active : null;
            static long? Cost(long? unit, long amount) => unit is long value ? checked(value * amount) : null;
            lines.Add(new(material.Item, info.Name, info.Icon, material.PerCraft, needed, owned, used, missing, bound, price,
                Cost(price, used), Cost(price, missing), Cost(price, needed)));
        }

        static long? Sum(IReadOnlyList<CraftQuoteLine> rows, Func<CraftQuoteLine, long> quantity, Func<CraftQuoteLine, long?> cost)
            => rows.Any(r => quantity(r) > 0 && cost(r) is null) ? null : rows.Sum(r => cost(r) ?? 0);
        var result = InventoryWatch.Info(recipe.Result);
        return new(recipeId, recipe.Result, result.Name, result.Icon, requested, crafts, produced, server, inventory.Complete,
            board.FetchedAt, board.Rows.Count > 0 ? board.Rows.Max(r => r.At) : null, lines,
            Sum(lines, r => r.Used, r => r.OwnedValue), Sum(lines, r => r.Missing, r => r.BuyCost), Sum(lines, r => r.Required, r => r.TotalValue));
    }
}
