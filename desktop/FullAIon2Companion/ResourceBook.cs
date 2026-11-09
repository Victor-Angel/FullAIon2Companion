using System.IO;
using System.Text.Json;

namespace FullAIon2Companion;

/// <summary>
/// Recursos: the last known coins, entries and limits of each character, kept on this PC (resources.json). The game says
/// them only at login (inventory 0x1156, counters 0x0B61) and then sends the changes, so what was seen stays shown with
/// its time until the character logs in again.
/// </summary>
public sealed class ResourceBook
{
    public sealed record Entry(string Character, int? Server, DateTime? CountersAt, Dictionary<string, long[]> Counters, DateTime? CoinsAt, Dictionary<string, long> Coins);
    readonly Dictionary<string, Entry> book = Load();
    int countersVersion = -1, coinsVersion = -1;
    string? lastKey;
    static string FilePath => Path.Combine(Program.DataDirectory, "resources.json");
    static Dictionary<string, Entry> Load()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(FilePath)) ?? []; } catch { }
        return [];
    }
    void Save() { try { File.WriteAllText(FilePath, JsonSerializer.Serialize(book)); } catch { } }

    /// <summary>Coins worth showing: the 9301000xx currencies of the item table plus the hidden cube key.</summary>
    public static bool IsCoin(int item) => item is >= 930100000 and < 930200000 || item == CubeKey;
    /// <summary>Counters identified against the game (key → name, icon). 1: entering the Shugo Festival at 23:05 on
    /// 2026-10-13 took it from 10 (+1) to 9 (+1); nothing else changed.</summary>
    public static readonly Dictionary<long, (string Name, string Icon)> Known = new()
    {
        [1] = ("Entradas al Festival de Shugo", "/assets/game/Icon_Add_Ticket_Dungeon_001.webp"),
    };
    public const int CubeKey = 630900003, Kinah = InventoryWatch.Kinah, BoundKinah = 930100001, OdCounter = 60000001;

    /// <summary>Takes the live state for the character in play (called on every panel tick; writes only on changes).</summary>
    public void Update(string? character, int? server, GameResources counters, InventoryWatch inventory)
    {
        if (string.IsNullOrWhiteSpace(character)) return;
        string key = $"{server}:{character}".ToLowerInvariant();
        if (key != lastKey) { lastKey = key; countersVersion = coinsVersion = -1; }
        var entry = book.GetValueOrDefault(key) ?? new(character, server, null, [], null, []);
        bool changed = false;
        if (counters.Version != countersVersion && counters.At is DateTime at)
        {
            countersVersion = counters.Version;
            var now = counters.Counters;
            // A change alone (0C61) updates what the login list said; with no login list seen it adds to the stored one.
            // Each counter keeps when the game last sent it (4th field, Unix seconds): the Od regenerates from it, and another
            // counter's change must not make an old Od value look new. Counters saved by 1.0.26 have no 4th field: they get the
            // entry's CountersAt (when they were saved) before it moves on, or the Od would take the time of any later change.
            var merged = counters.Complete ? [] : entry.Counters.ToDictionary(e => e.Key, e =>
                e.Value.Length != 3 || entry.CountersAt is not DateTime saved ? e.Value
                    : new[] { e.Value[0], e.Value[1], e.Value[2], new DateTimeOffset(saved).ToUnixTimeSeconds() });
            // The time each counter came in its own frame (a value sent again unchanged is a new reading too).
            foreach (var c in now.Values)
                merged[c.Key.ToString()] = [c.Value, c.Extra, c.Long ? 1 : 0, new DateTimeOffset(c.At == default ? at : c.At).ToUnixTimeSeconds()];
            entry = entry with { Counters = merged, CountersAt = at };
            changed = true;
        }
        if (inventory.Version != coinsVersion)
        {
            coinsVersion = inventory.Version;
            var coins = inventory.Holdings(IsCoin);
            if (coins.Count > 0)
            {
                var merged = new Dictionary<string, long>(entry.Coins);
                foreach (var (item, count) in coins) merged[item.ToString()] = count;
                entry = entry with { Coins = merged, CoinsAt = DateTime.Now };
                changed = true;
            }
        }
        if (!changed) return;
        book[key] = entry with { Character = character, Server = server };
        Save();
    }

    /// <summary>The panel's view: every character, the one in play first, with its coins and counters.</summary>
    public object View(string? character, int? server, int odMax = 840)
    {
        string? live = string.IsNullOrWhiteSpace(character) ? null : $"{server}:{character}".ToLowerInvariant();
        return book.OrderByDescending(e => e.Key == live).ThenByDescending(e => e.Value.CountersAt ?? e.Value.CoinsAt).Select(e =>
        {
            var v = e.Value;
            long kinah = v.Coins.GetValueOrDefault(Kinah.ToString());
            // Kina (Bound) is an inventory item (2026-10-13 22:41: 276,000); counter 3 (6,649,000) is something else.
            long? bound = v.Coins.TryGetValue(BoundKinah.ToString(), out var b) ? b : null;
            // The Od with its regenerations since it was sent (OdEnergy.Project), like the overlay.
            var od = v.Counters.TryGetValue(OdCounter.ToString(), out var o) ? OdView(o, v.CountersAt, odMax) : null;
            var coins = v.Coins.Where(c => c.Key != Kinah.ToString() && c.Key != BoundKinah.ToString()).Select(c =>
            {
                var info = InventoryWatch.Info(int.Parse(c.Key));
                return new { id = int.Parse(c.Key), name = info.Name, icon = info.Icon, count = c.Value };
            }).OrderBy(c => c.id == CubeKey ? 0 : 1).ThenBy(c => c.id).ToList();
            // Only the counters identified against the game are shown; the rest stay in resources.json to be named later.
            var counters = v.Counters.Where(c => c.Key != OdCounter.ToString() && Known.ContainsKey(long.Parse(c.Key)))
                .Select(c => new { key = long.Parse(c.Key), name = Known.TryGetValue(long.Parse(c.Key), out var k) ? k.Name : null, icon = Known.TryGetValue(long.Parse(c.Key), out var ki) ? ki.Icon : null, value = c.Value[0], extra = c.Value[1], time = c.Value[2] == 1 })
                .OrderBy(c => c.name is null ? 1 : 0).ThenBy(c => c.key).ToList();
            return new { key = e.Key, live = e.Key == live, character = v.Character, server = v.Server, serverName = v.Server is int s ? ServerName(s) : null,
                countersAt = v.CountersAt, coinsAt = v.CoinsAt, kinah = v.Coins.ContainsKey(Kinah.ToString()) ? kinah : (long?)null, bound, od, coins, counters };
        }).ToList();
    }
    static string? ServerName(int id) => RoomRoster.ServerName(id);
    static object OdView(long[] o, DateTime? countersAt, int max)
    {
        DateTime? seen = o.Length > 3 ? DateTimeOffset.FromUnixTimeSeconds(o[3]).LocalDateTime : countersAt;
        var (natural, projected) = seen is DateTime at ? OdEnergy.Project((int)o[0], at, DateTime.Now, max) : ((int)o[0], false);
        return new { natural, extra = o[1], estimated = projected, seen };
    }
}
