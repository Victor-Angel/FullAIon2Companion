using System.Buffers.Binary;
using System.IO;
using System.Text.Json;

namespace FullAIon2Companion;

/// <summary>
/// The crafting professions as the server tells them, for the overlay's crafting card (2026-11-01, the user: «un nuevo overlay
/// de herrería para la experiencia, nivel y cuánto gano de EXP y qué nivel va», with the game's window «Herrería · Principiante
/// nv. 44 · 45/413»). The profession is 1 Herrería, 2 Artesanía, 3 Alquimia, 4 Armería, 5 Cocina (the first digit of the
/// recipe ids of each one; the names of their workshops on the map):
/// <code>
/// own character record 0x3336   … | 05 | 5 × (profession u8 | level u16 | EXP u32 | 6 bytes)   at every login and map load
/// 6F 8D | 00 05 | varint | recipe u32 | …          a craft starts (125016001 Lingote de oricalco, 524086002 ensalada de frutas)
/// 7C 8D | profession u8 | EXP u32 | 4 | gain u32 | 4   its EXP: the EXP now and what the craft gave (+20, +112, +26, +33…)
/// 7D 8D | profession u8 | level u16 | EXP u32 | …     a level up, with the EXP left over (just after its 0x7C8D)
/// </code>
/// The level is the profession's whole level, 1 to 100: the game shows 1–50 as «Principiante nv. 1–50» and 51–100 as
/// «Especialista nv. 1–50» (2026-11-01: 51 was «Especialista nv. 1 · 203/602»). Level 50 is a cap until the promotion quest.
/// The game does not send the EXP a level needs: it is in <see cref="Table"/>, the same for every profession. A level up the
/// app sees also gives it (the EXP before the last craft plus what it gave, minus what is left over), and that one wins
/// (craft-sessions.json, for every character of this PC), in case a patch changes the table. The session counts from the first craft after a reset, per character:
/// crafts, EXP and levels per profession and what was crafted; its time leaves out pauses over <see cref="Pause"/>.
/// </summary>
public sealed class CraftWatch
{
    const int Record = 0x3336, Start = 0x6F8D, Exp = 0x7C8D, LevelUp = 0x7D8D;
    public static readonly string[] Names = ["", "Herrería", "Artesanía", "Alquimia", "Armería", "Cocina"];
    /// <summary>A longer gap between crafts is a pause: it does not count as time crafting.</summary>
    public static readonly TimeSpan Pause = TimeSpan.FromMinutes(5);
    /// <summary>
    /// The EXP each level needs (index = level; 0 at 50 and 100, the caps), every profession alike. From questlog.gg's crafting
    /// mastery table (2026-11-01), which matches every level up seen in the game: Cocina 1–21 (2026-10-25), Herrería 38, 39, 41,
    /// 42, 46–49 and 43 (five crafts of 20), and the game's own window at 44 («45/413») and 51 («203/602»).
    /// </summary>
    public static readonly long[] Table =
    [
        0, 26, 28, 32, 34, 32, 33, 35, 36, 38, 40, 42, 44, 46, 48, 54, 59, 62, 66, 69, 72,
        113, 119, 125, 131, 138, 145, 151, 159, 167, 175, 186, 198, 210, 224, 238, 253, 269, 286, 304, 323,
        344, 365, 388, 413, 439, 467, 496, 527, 560, 0,
        602, 621, 639, 660, 680, 701, 722, 745, 768, 792, 820, 850, 881, 913, 945, 980, 1015, 1052, 1089, 1137,
        1186, 1236, 1291, 1346, 1404, 1465, 1528, 1593, 1663, 1735, 1809, 1888, 1970, 2053, 2143, 2235, 2330, 2432, 2537, 2645,
        2761, 2879, 3002, 3133, 3268, 3406, 3556, 3709, 3865, 0,
    ];
    /// <summary>The grade and its own level, as the game shows them: 1–50 «Principiante nv. 1–50», 51–100 «Especialista nv. 1–50».</summary>
    public static (string Grade, int Level) Grade(int level) => level > 50 ? ("Especialista", level - 50) : ("Principiante", level);

    public sealed class Level
    {
        /// <summary>0 while not known (an EXP frame came before any record this character's).</summary>
        public int Value { get; set; }
        public long Exp { get; set; }
        public DateTime At { get; set; }
    }
    public sealed class Progress
    {
        public int Crafts { get; set; }
        public long Exp { get; set; }
        public int LevelUps { get; set; }
        /// <summary>The level when the session first crafted in this profession.</summary>
        public int From { get; set; }
        /// <summary>What the last craft in it gave (the crafts left to the next level are counted with it).</summary>
        public long Last { get; set; }
    }
    public sealed class Session
    {
        public DateTime Start { get; set; }
        public DateTime Last { get; set; }
        /// <summary>Seconds crafting: the gaps between crafts, the pauses left out.</summary>
        public double Active { get; set; }
        public Dictionary<int, Progress> Professions { get; set; } = [];
        /// <summary>How many times each recipe was crafted.</summary>
        public Dictionary<int, int> Recipes { get; set; } = [];
    }
    sealed class Book
    {
        public Dictionary<int, Level> Levels { get; set; } = [];
        public Session? Session { get; set; }
    }
    sealed class Store
    {
        public Dictionary<string, Book> Books { get; set; } = [];
        /// <summary>The EXP each level needs, learned at level ups (profession → level → EXP).</summary>
        public Dictionary<int, Dictionary<int, long>> Needed { get; set; } = [];
    }
    /// <summary>One profession on the card: its level and EXP (null before the game said them), the EXP the level needs (null
    /// while not known), and the session's progress in it.</summary>
    public sealed record Line(int Profession, int? Level, long? Exp, long? Needed, Progress? Session, DateTime? At);
    /// <summary>What the card shows: every profession known, the one crafted last first, and the session (null before a craft).</summary>
    public sealed record View(IReadOnlyList<Line> Professions, int? Current, Session? Session, DateTime? LastCraft);

    readonly object gate = new(), saveGate = new();
    Store store = new();
    string? path;
    string key = "";
    DateTime at, lastSave = DateTime.MinValue, lastCraft = DateTime.MinValue, startedAt = DateTime.MinValue;
    // The EXP the server sent since the app started, per profession: the only base a level's need is learned from.
    readonly Dictionary<int, (int? Level, long Exp)> live = [];
    // The last EXP frame (its profession, the EXP before it, its gain, its time), for the level up that follows it.
    (int Profession, long Before, long Gain, DateTime At)? lastGain;
    // The record's professions waiting for the engine to say whose record it was (it parses the record after this watch).
    Dictionary<int, (int Level, long Exp)>? pendingRecord;
    DateTime pendingAt;
    int? current, startedRecipe;
    bool dirty;
    /// <summary>Raises with every change.</summary>
    public int Version { get; private set; }

    public void Open(string file)
    {
        path = file;
        try { store = JsonSerializer.Deserialize<Store>(File.ReadAllText(file)) ?? new(); } catch { store = new(); }
        store.Books ??= []; store.Needed ??= [];
    }
    void Save()
    {
        if (path is null) return;
        // One save at a time from taking the state to writing it (the capture thread and the panel's «Reiniciar»). gate is
        // never held when saveGate is taken.
        lock (saveGate)
        {
            string json;
            lock (gate) { json = JsonSerializer.Serialize(store); dirty = false; lastSave = at; }
            try { File.WriteAllText(path + ".tmp", json); File.Move(path + ".tmp", path, true); } catch { }
        }
    }
    /// <summary>On closing: what changed since the last save is written.</summary>
    public void Flush() { bool pending; lock (gate) pending = dirty; if (pending) Save(); }
    Book Current() { if (!store.Books.TryGetValue(key, out var b)) store.Books[key] = b = new(); return b; }

    /// <summary>
    /// The character the frames belong to (server and name). What came before it was known joins its book. From the engine when
    /// the game sends the character record (<paramref name="record"/>): the professions of that record are this character's.
    /// </summary>
    public void SetCharacter(int? server, string? name, bool record = false)
    {
        if (server is not int s || s <= 0 || string.IsNullOrEmpty(name)) return;
        string next = $"{s}:{name.ToLowerInvariant()}";
        bool changed = false;
        lock (gate)
        {
            if (next != key)
            {
                var before = key; key = next; changed = true;
                // Another character: the EXP seen was the other one's, and its craft is not this one's.
                if (before.Length > 0) { live.Clear(); lastGain = null; current = null; startedRecipe = null; }
                else if (store.Books.Remove("", out var early))
                {
                    var book = Current();
                    foreach (var (p, l) in early.Levels) if (!book.Levels.TryGetValue(p, out var kept) || kept.At < l.At) book.Levels[p] = l;
                    if (early.Session is { } e) book.Session = Merge(book.Session, e);
                }
            }
            if (record && pendingRecord is { } levels && (at - pendingAt).Duration() <= TimeSpan.FromSeconds(5))
            {
                pendingRecord = null; changed = true;
                var book = Current();
                foreach (var (p, (level, exp)) in levels)
                {
                    book.Levels[p] = new Level { Value = level, Exp = exp, At = at };
                    live[p] = (level, exp);
                }
            }
            if (changed) Version++;
        }
        if (changed) Save();
    }
    static Session Merge(Session? into, Session add)
    {
        if (into is null) return add;
        into.Start = add.Start < into.Start ? add.Start : into.Start; into.Last = add.Last > into.Last ? add.Last : into.Last;
        into.Active += add.Active;
        foreach (var (p, g) in add.Professions)
        {
            if (!into.Professions.TryGetValue(p, out var mine)) { into.Professions[p] = g; continue; }
            mine.Crafts += g.Crafts; mine.Exp += g.Exp; mine.LevelUps += g.LevelUps; mine.Last = g.Last;
            if (mine.From == 0 || g.From > 0 && g.From < mine.From) mine.From = g.From;
        }
        foreach (var (r, n) in add.Recipes) into.Recipes[r] = into.Recipes.GetValueOrDefault(r) + n;
        return into;
    }

    public void Add(ReadOnlySpan<byte> frame, DateTime timestamp) { at = timestamp; Frames.Walk(frame, Parse); }
    void Parse(ReadOnlySpan<byte> f)
    {
        if (dirty && at - lastSave > TimeSpan.FromSeconds(5)) Save();
        if (f.Length < 2) return;
        int opcode = BinaryPrimitives.ReadUInt16BigEndian(f);
        if (opcode == Record) { if (ReadRecord(f) is { } levels) lock (gate) { pendingRecord = levels; pendingAt = at; } }
        else if (opcode == Start && f.Length >= 11) Started(f);
        else if (opcode == Exp && f.Length >= 15 && f[2] is >= 1 and <= 5) Gained(f[2], BinaryPrimitives.ReadUInt32LittleEndian(f[3..]), BinaryPrimitives.ReadUInt32LittleEndian(f[11..]));
        else if (opcode == LevelUp && f.Length >= 9 && f[2] is >= 1 and <= 5) Leveled(f[2], BinaryPrimitives.ReadUInt16LittleEndian(f[3..]), BinaryPrimitives.ReadUInt32LittleEndian(f[5..]));
    }

    /// <summary>
    /// The professions of the own character record: a count of 5, then each profession in order (1 to 5) with its level u16, its
    /// EXP u32 and 6 more bytes. Found by that shape near the start of the record (offset 75–76 for Darkni: it moves with the
    /// name), so no other part of the record is read as one.
    /// </summary>
    public static Dictionary<int, (int Level, long Exp)>? ReadRecord(ReadOnlySpan<byte> f)
    {
        for (int i = 20; i + 1 + 5 * 13 <= f.Length && i < 400; i++)
        {
            if (f[i] != 5) continue;
            var levels = new Dictionary<int, (int, long)>();
            for (int p = 1; p <= 5; p++)
            {
                int o = i + 1 + (p - 1) * 13;
                int level = BinaryPrimitives.ReadUInt16LittleEndian(f[(o + 1)..]);
                long exp = BinaryPrimitives.ReadUInt32LittleEndian(f[(o + 3)..]);
                if (f[o] != p || level is < 1 or > 200 || exp > 10_000_000) break;
                levels[p] = (level, exp);
            }
            if (levels.Count == 5) return levels;
        }
        return null;
    }
    void Started(ReadOnlySpan<byte> f)
    {
        // 00 05 | a varint (3 bytes so far) | the recipe.
        int o = 4;
        while (o < f.Length && (f[o] & 0x80) != 0) o++;
        o++;
        if (o + 4 > f.Length) return;
        int recipe = BinaryPrimitives.ReadInt32LittleEndian(f[o..]);
        if (recipe is < 100_000_000 or > 599_999_999) return;
        lock (gate) { startedRecipe = recipe; startedAt = at; Version++; }
    }
    void Gained(int profession, long exp, long gain)
    {
        if (gain is < 0 or > 1_000_000 || exp > 10_000_000) return;
        lock (gate)
        {
            var book = Current();
            // The EXP before this craft: the one the server said last in this run, else not known (and no level is learned). At a
            // level up the EXP now is what is left over, so below before + gain; above it, frames were missed.
            long? before = live.TryGetValue(profession, out var l) && l.Exp + gain >= exp ? l.Exp : null;
            lastGain = before is long b ? (profession, b, gain, at) : null;
            // The level as this run's record or level ups said it (a level is only learned from it), else the one kept.
            var kept = book.Levels.GetValueOrDefault(profession);
            int? liveLevel = live.TryGetValue(profession, out var known) ? known.Level : null;
            int? level = liveLevel ?? (kept is { Value: > 0 } ? kept.Value : null);
            live[profession] = (liveLevel, exp);
            kept ??= book.Levels[profession] = new Level();
            kept.Exp = exp; kept.At = at;

            var s = book.Session ??= new Session { Start = at, Last = at };
            if (at > s.Last && at - s.Last <= Pause) s.Active += (at - s.Last).TotalSeconds;
            if (at > s.Last) s.Last = at;
            if (!s.Professions.TryGetValue(profession, out var g)) s.Professions[profession] = g = new Progress { From = level ?? 0 };
            if (g.From == 0 && level is int from) g.From = from;
            g.Crafts++; g.Exp += gain; g.Last = gain;
            // What it made: the recipe this craft started with (its 0x6F8D, seconds before).
            if (startedRecipe is int recipe && at - startedAt <= TimeSpan.FromMinutes(1)) { s.Recipes[recipe] = s.Recipes.GetValueOrDefault(recipe) + 1; startedRecipe = null; }
            current = profession; lastCraft = at;
            dirty = true; Version++;
        }
    }
    void Leveled(int profession, int level, long exp)
    {
        if (level is < 1 or > 200 || exp > 10_000_000) return;
        lock (gate)
        {
            var book = Current();
            // The level's need, when the craft before it came from a known EXP: before + gain − left over.
            if (lastGain is { } g && g.Profession == profession && (at - g.At).Duration() <= TimeSpan.FromSeconds(1)
                && live.TryGetValue(profession, out var l) && l.Level == level - 1)
            {
                long needed = g.Before + g.Gain - exp;
                // Not at the caps: reaching 50 (or 100) the EXP left over is dropped, and 51 comes from the promotion quest.
                if (needed > g.Before && level is not (50 or 51 or 100))
                {
                    if (!store.Needed.TryGetValue(profession, out var table)) store.Needed[profession] = table = [];
                    table[level - 1] = needed;
                }
            }
            lastGain = null;
            live[profession] = (level, exp);
            book.Levels[profession] = new Level { Value = level, Exp = exp, At = at };
            if (book.Session is { } s && s.Professions.TryGetValue(profession, out var p)) p.LevelUps++;
            Version++;
        }
        Save();
    }

    static Dictionary<int, (int Item, int Count)>? products;
    /// <summary>What a recipe makes: its item and how many (data/craft-recipes.json, from the encyclopedia's recipes).</summary>
    public static (int Item, int Count)? Product(int recipe)
    {
        products ??= LoadProducts();
        return products.TryGetValue(recipe, out var p) ? p : null;
    }
    static Dictionary<int, (int, int)> LoadProducts()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, int[]>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "craft-recipes.json")))!.Where(p => p.Value.Length >= 2).ToDictionary(p => int.Parse(p.Key), p => (p.Value[0], p.Value[1])); }
        catch { return []; }
    }

    /// <summary>The EXP a level of a profession needs: learned on this PC, else seen before; null while not known.</summary>
    public long? NeededFor(int profession, int level)
    {
        lock (gate) return Needed(profession, level);
    }
    long? Needed(int profession, int level) =>
        store.Needed.TryGetValue(profession, out var t) && t.TryGetValue(level, out var n) ? n
        : level >= 1 && level < Table.Length ? Table[level] : null;

    /// <summary>A new session: the next craft starts it.</summary>
    public void Reset()
    {
        lock (gate) { Current().Session = null; Version++; }
        Save();
    }

    public View Now()
    {
        lock (gate)
        {
            store.Books.TryGetValue(key, out var book);
            var s = book?.Session;
            var copy = s is null ? null : new Session { Start = s.Start, Last = s.Last, Active = s.Active, Recipes = new(s.Recipes),
                Professions = s.Professions.ToDictionary(p => p.Key, p => new Progress { Crafts = p.Value.Crafts, Exp = p.Value.Exp, LevelUps = p.Value.LevelUps, From = p.Value.From, Last = p.Value.Last }) };
            var lines = new List<Line>();
            for (int p = 1; p <= 5; p++)
            {
                var level = book?.Levels.GetValueOrDefault(p);
                var progress = copy?.Professions.GetValueOrDefault(p);
                if (level is null && progress is null) continue;
                int? value = level is { Value: > 0 } ? level.Value : null;
                lines.Add(new(p, value, level?.Exp, value is int v ? Needed(p, v) : null, progress, level?.At));
            }
            // The profession crafted last first, then the ones crafted this session, then by level.
            int? last = current ?? copy?.Professions.OrderByDescending(p => p.Value.Crafts).Select(p => (int?)p.Key).FirstOrDefault();
            lines = lines.OrderByDescending(l => l.Profession == last).ThenByDescending(l => l.Session is not null).ThenByDescending(l => l.Level ?? 0).ToList();
            return new(lines, last, copy, lastCraft == DateTime.MinValue ? null : lastCraft);
        }
    }
}
