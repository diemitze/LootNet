using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using LootNet.UI;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Comfort.Common;

namespace LootNet.Services
{
    public class RaidStats
    {
        public double TotalFoundValue;
        public int ItemsFound;
        public int PmcKills;
        public int ScavKills;
        public List<(string Name, double Value)> TopItems = new();
        public List<(string Name, int Kills)> FireteamMembers;
        public string MapName;
        public DateTime RaidTime;
        public bool IsScavRaid;
        public bool PlayerSurvived = true;
        public int XpEarned;
        public int XpBonus;
        public string PlayerName;
    }

    public class RaidTracker : MonoBehaviour
    {
        public static RaidTracker Instance { get; private set; }

        public static HashSet<string> SpawnedItemIds { get; } = new();
        public static bool IsScavRaid { get; private set; }
        public static bool IsInRaid   { get; private set; }
        public static bool PlayerDied { get; private set; }
        public static int  LatestSessionXp => _latestSessionXp;

        public static bool ExtraPollWindow { get; private set; }

        public static readonly List<RaidStats> RaidHistory = new();
        internal const int MaxHistory = 15;

        public static event Action<RaidStats> OnLocalSummaryBuilt;

        public static Func<bool> IsTeamRaid;

        public static Func<int> ExpectedTeammates;

        private static readonly Dictionary<string, (string Name, string TemplateId, double Value)> _foundItems = new();

        private static readonly Dictionary<string, int> _spawnStackUnits = new();
        private static readonly Dictionary<string, (string Name, int Units)> _foundStacks = new();
        private static readonly Dictionary<string, string> _stackNames = new();
        private static readonly HashSet<string> _excludedStackTemplates = new();
        private static readonly Dictionary<string, bool> _stackableCache = new();
        private static readonly Dictionary<string, (string Name, int Kills)> _botKills = new();
        private static readonly HashSet<string> _recentKillIds = new();
        private static readonly HashSet<string> _confirmedFollowerIds = new();
        private static int _pmcKills;
        private static int _scavKills;
        private static string _pendingMapName;
        private static DateTime _pendingRaidTime;
        private static int _pendingStartXp;
        private static int _latestSessionXp;

        private static object _cachedProfileForPolling;

        private static bool _pitResolved;
        private static MethodInfo _pitIsFollower;
        private static Func<string, bool> _isFollower;

        public static double DisplayValue
        {
            get
            {
                RecountStacks();
                double sum = 0;
                foreach (var kv in _foundItems) sum += kv.Value.Value;
                foreach (var kv in _foundStacks) sum += kv.Value.Units * Plugin.PriceService.GetPrice(kv.Key);
                return sum;
            }
        }

        private void Awake() => Instance = this;

        public static System.Collections.IEnumerator PollLoop()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(ExtraPollWindow ? 0.2f : 1f);

                if (!IsInRaid && !ExtraPollWindow) continue;

                Player mp = null;
                try { mp = Singleton<GameWorld>.Instance?.MainPlayer; } catch { }

                object profileToUse = mp?.Profile ?? _cachedProfileForPolling;
                if (profileToUse == null) continue;

                int v = 0;
                try { v = TryReadSessionXpFromProfile(profileToUse); }
                catch (Exception ex) { Plugin.LogSource.LogWarning($"[LootNet] XP poll threw: {ex.Message}"); }

                if (v > _latestSessionXp) _latestSessionXp = v;
            }
        }

        private static void ClearRaidState()
        {
            SpawnedItemIds.Clear();
            _foundItems.Clear();
            _spawnStackUnits.Clear();
            _foundStacks.Clear();
            _stackNames.Clear();
            _excludedStackTemplates.Clear();
            _botKills.Clear();
            _recentKillIds.Clear();
            _confirmedFollowerIds.Clear();
            _pmcKills  = 0;
            _scavKills = 0;
            PlayerDied = false;

        }

        public static void MarkPlayerDied() => PlayerDied = true;

        public static void OnRaidEntered(Player player)
        {
            if (player == null) return;

            ClearRaidState();
            TeamSummaryStore.Clear();
            _latestSessionXp = 0;
            _cachedProfileForPolling = player.Profile;
            IsScavRaid      = player.Side == EPlayerSide.Savage;
            IsInRaid        = true;
            _pendingMapName = TryGetMapName();
            _pendingRaidTime = DateTime.Now;
            _pendingStartXp = TryReadExperience(player);

            TryReadSessionXp(player);
            ResolvePit();

            var spawnSlots = EPlayerItems.InRaidItems;

            if (IsScavRaid)
            {

                foreach (var item in player.Inventory.GetPlayerItems(spawnSlots))
                    foreach (var child in item.GetAllItems())
                        TrackItemAdded(child);
            }
            else
            {
                foreach (var item in player.Inventory.GetPlayerItems(spawnSlots))
                    foreach (var child in item.GetAllItems())
                    {
                        if (IsStackable(child))
                        {
                            string tpl = child.TemplateId.ToString();
                            _spawnStackUnits.TryGetValue(tpl, out int units);
                            _spawnStackUnits[tpl] = units + child.StackObjectsCount;
                        }
                        else
                        {
                            SpawnedItemIds.Add(child.Id.ToString());
                        }
                    }
            }
        }

        public static void TrackItemAdded(Item item)
        {
            if (IsStackable(item))
            {
                RegisterStackTemplate(item);
                return;
            }

            string id = item.Id.ToString();
            if (!IsScavRaid && SpawnedItemIds.Contains(id)) return;
            if (_foundItems.ContainsKey(id)) return;
            if (IsQuestItem(item)) return;
            if (Plugin.DefaultFleaRules.Value && !CanSellOnFlea(item)) return;

            string templateId = item.TemplateId.ToString();
            double price = Plugin.PriceService.IsLoaded ? Plugin.PriceService.GetPrice(templateId) : 0;
            _foundItems[id] = (item.LocalizedName(), templateId, price);
        }

        private static bool IsStackable(Item item)
        {
            string tpl = item.TemplateId.ToString();
            if (_stackableCache.TryGetValue(tpl, out bool cached)) return cached;

            bool stackable = false;
            try { stackable = item.StackMaxSize > 1; } catch { }
            _stackableCache[tpl] = stackable;
            return stackable;
        }

        private static void RegisterStackTemplate(Item item)
        {
            string tpl = item.TemplateId.ToString();
            if (_stackNames.ContainsKey(tpl) || _excludedStackTemplates.Contains(tpl)) return;

            if (IsQuestItem(item) || (Plugin.DefaultFleaRules.Value && !CanSellOnFlea(item)))
            {
                _excludedStackTemplates.Add(tpl);
                return;
            }
            _stackNames[tpl] = item.LocalizedName();
        }

        private static void RecountStacks()
        {
            Player mp = null;
            try { mp = Singleton<GameWorld>.Instance?.MainPlayer; } catch { }
            if (mp == null) return;

            var counts = new Dictionary<string, int>();
            try
            {
                foreach (var item in mp.Inventory.GetPlayerItems(EPlayerItems.InRaidItems))
                    foreach (var child in item.GetAllItems())
                    {
                        if (!IsStackable(child)) continue;
                        RegisterStackTemplate(child);

                        string tpl = child.TemplateId.ToString();
                        if (!_stackNames.ContainsKey(tpl)) continue;

                        counts.TryGetValue(tpl, out int c);
                        counts[tpl] = c + child.StackObjectsCount;
                    }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[LootNet] RecountStacks failed: {ex.Message}");
                return;
            }

            _foundStacks.Clear();
            foreach (var kv in counts)
            {
                _spawnStackUnits.TryGetValue(kv.Key, out int spawn);
                int found = kv.Value - spawn;
                if (found > 0) _foundStacks[kv.Key] = (_stackNames[kv.Key], found);
            }
        }

        private static bool IsQuestItem(Item item)
        {
            try
            {
                if (item == null) return false;
                if (item.QuestItem) return true;

                var tmpl = item.Template;
                if (tmpl == null) return false;
                var tmplType = tmpl.GetType();
                var qf = tmplType.GetField("QuestItem") ?? tmplType.GetField("_questItem");
                if (qf?.GetValue(tmpl) is bool tqi && tqi) return true;
            }
            catch { }
            return false;
        }

        private static bool CanSellOnFlea(Item item)
        {
            try
            {
                if (item == null) return true;

                var tmpl = item.Template;
                if (tmpl == null) return true;
                var tmplType = tmpl.GetType();

                var canField = tmplType.GetField("CanSellOnRagfair");
                if (canField?.GetValue(tmpl) is bool cs) return cs;

                var propsObj = tmplType.GetField("Props")?.GetValue(tmpl)
                            ?? tmplType.GetProperty("Props")?.GetValue(tmpl);
                if (propsObj != null)
                {
                    var pType = propsObj.GetType();
                    var pField = pType.GetField("CanSellOnRagfair");
                    if (pField?.GetValue(propsObj) is bool pcs) return pcs;
                    var pProp = pType.GetProperty("CanSellOnRagfair");
                    if (pProp?.GetValue(propsObj) is bool ppcs) return ppcs;
                }
            }
            catch { }
            return true;
        }

        public static void RefreshPrices()
        {
            var keys = _foundItems.Keys.ToList();
            foreach (var key in keys)
            {
                var (name, templateId, _) = _foundItems[key];
                _foundItems[key] = (name, templateId, Plugin.PriceService.GetPrice(templateId));
            }
        }

        public static void TrackItemRemoved(Item item)
        {
            if (IsStackable(item)) return;

            string id = item.Id.ToString();
            if (!IsScavRaid && SpawnedItemIds.Contains(id)) return;
            _foundItems.Remove(id);
        }

        public static bool MarkKillSeen(string profileId) => _recentKillIds.Add(profileId);

        public static void TrackKill(bool isPmc)
        {
            if (isPmc) _pmcKills++;
            else       _scavKills++;
        }

        public static void TrackBotKill(IPlayer bot)
        {
            string id   = bot.ProfileId;
            string name = bot.Profile?.Nickname ?? (bot as UnityEngine.Object)?.name ?? "Follower";
            _botKills.TryGetValue(id, out var existing);
            _botKills[id] = (name, existing.Kills + 1);

            if (_isFollower != null && !_confirmedFollowerIds.Contains(id))
            {
                try { if (_isFollower(id)) _confirmedFollowerIds.Add(id); }
                catch { }
            }
        }

        public static RaidStats ComputeStats()
        {
            RecountStacks();
            bool hasLoot  = _foundItems.Count > 0 || _foundStacks.Count > 0;
            bool hasKills = _pmcKills > 0 || _scavKills > 0;
            bool survived = !PlayerDied;
            if (survived)
            {
                try
                {
                    var mp = Singleton<GameWorld>.Instance?.MainPlayer;
                    if (mp?.HealthController != null && !mp.HealthController.IsAlive) survived = false;
                }
                catch { }
            }

            int xpEarned = ComputeXpEarned();

            if (!hasLoot && !hasKills && xpEarned <= 0 && survived) return null;

            var allFound = _foundItems.Values.Where(x => x.Value > 0).ToList();

            var stackFound = _foundStacks
                .Select(kv => (kv.Value.Name, Units: kv.Value.Units,
                               Total: kv.Value.Units * Plugin.PriceService.GetPrice(kv.Key)))
                .Where(x => x.Total > 0)
                .ToList();

            var grouped = allFound
                .GroupBy(x => x.TemplateId)
                .Select(g =>
                {
                    int    count = g.Count();
                    double total = g.Sum(x => x.Value);
                    string name  = count > 1 ? $"{g.First().Name} x{count}" : g.First().Name;
                    return (name, total);
                })
                .Concat(stackFound.Select(x => (name: $"{x.Name} x{x.Units}", total: x.Total)))
                .OrderByDescending(x => x.total)
                .ToList();

            int bonus = TryReadCounterTag("ExpExitStatus");
            int baseXp = xpEarned - bonus;
            if (baseXp < 0) baseXp = xpEarned;

            return new RaidStats
            {
                ItemsFound      = allFound.Count + stackFound.Count,
                TotalFoundValue = allFound.Sum(x => x.Value) + stackFound.Sum(x => x.Total),
                PmcKills        = _pmcKills,
                ScavKills       = _scavKills,
                TopItems        = grouped.Take(5).ToList(),
                FireteamMembers = TryGetFireteamStats(),
                MapName         = _pendingMapName ?? "Unknown",
                RaidTime        = _pendingRaidTime,
                IsScavRaid      = IsScavRaid,
                PlayerSurvived  = survived,
                XpEarned        = baseXp,
                XpBonus         = bonus,
            };
        }

        public static RaidStats BuildPendingStats()
        {
            var stats = ComputeStats();
            if (stats == null) return null;

            if (RaidHistory.Count >= MaxHistory) RaidHistory.RemoveAt(0);
            RaidHistory.Add(stats);

            if (Plugin.PersistRaidHistory != null && Plugin.PersistRaidHistory.Value)
                SavePersistedHistory();

            try { OnLocalSummaryBuilt?.Invoke(stats); }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"[LootNet] OnLocalSummaryBuilt threw: {ex.Message}"); }

            return stats;
        }

        private static string GetHistoryFilePath()
        {
            var dir = System.IO.Path.Combine(BepInEx.Paths.PluginPath, "LootNet");
            try { System.IO.Directory.CreateDirectory(dir); } catch { }
            return System.IO.Path.Combine(dir, "raid_history.json");
        }

        public static void SavePersistedHistory()
        {
            try
            {
                var path = GetHistoryFilePath();
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(RaidHistory, Newtonsoft.Json.Formatting.Indented);
                System.IO.File.WriteAllText(path, json);
            }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"[LootNet] SavePersistedHistory failed: {ex.Message}"); }
        }

        public static void LoadPersistedHistory()
        {
            try
            {
                var path = GetHistoryFilePath();
                if (!System.IO.File.Exists(path)) return;
                var json = System.IO.File.ReadAllText(path);
                var loaded = Newtonsoft.Json.JsonConvert.DeserializeObject<List<RaidStats>>(json);
                if (loaded == null) return;

                RaidHistory.Clear();
                int start = Math.Max(0, loaded.Count - MaxHistory);
                for (int i = start; i < loaded.Count; i++) RaidHistory.Add(loaded[i]);
                Plugin.LogSource.LogInfo($"[LootNet] Loaded {RaidHistory.Count} raid(s) from persistent history.");
            }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"[LootNet] LoadPersistedHistory failed: {ex.Message}"); }
        }

        public static void ResetAfterRaid()
        {
            ClearRaidState();
            IsScavRaid = false;
            IsInRaid   = false;
        }

        public static void OpenExtraPollWindow(float seconds = 4f)
        {
            ExtraPollWindow = true;
            if (Plugin.Instance != null) Plugin.Instance.StartCoroutine(CloseExtraWindowAfter(seconds));
            else ExtraPollWindow = false;
        }

        private static System.Collections.IEnumerator CloseExtraWindowAfter(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            ExtraPollWindow = false;
        }

        private static List<(string Name, int Kills)> TryGetFireteamStats()
        {
            if (_isFollower == null) return null;
            if (_confirmedFollowerIds.Count == 0) return null;

            var result = new List<(string Name, int Kills)>();
            foreach (var id in _confirmedFollowerIds)
            {
                if (_botKills.TryGetValue(id, out var entry))
                    result.Add((entry.Name, entry.Kills));
            }

            result.Sort((a, b) => b.Kills.CompareTo(a.Kills));
            return result.Count > 0 ? result : null;
        }

        private static string TryGetMapName()
        {
            try
            {
                var gw = Singleton<GameWorld>.Instance;
                if (gw == null) return "Unknown";

                foreach (var name in new[] { "LocationId", "Location_0", "_locationId", "location" })
                {
                    var prop = typeof(GameWorld).GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (prop?.GetValue(gw) is string ps && !string.IsNullOrEmpty(ps))
                        return FormatMapName(ps);

                    var field = typeof(GameWorld).GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field?.GetValue(gw) is string fs && !string.IsNullOrEmpty(fs))
                        return FormatMapName(fs);
                }

                return "Unknown";
            }
            catch { return "Unknown"; }
        }

        private static int TryReadExperience(Player player)
        {
            try
            {
                if (player?.Profile == null) return 0;
                var profile = player.Profile;
                var profType = profile.GetType();
                var prop = profType.GetProperty("Experience", BindingFlags.Public | BindingFlags.Instance);
                if (prop?.GetValue(profile) is int e) return e;
                var field = profType.GetField("Experience", BindingFlags.Public | BindingFlags.Instance);
                if (field?.GetValue(profile) is int ef) return ef;

                var info = profType.GetProperty("Info", BindingFlags.Public | BindingFlags.Instance)?.GetValue(profile);
                if (info != null)
                {
                    var ip = info.GetType().GetProperty("Experience", BindingFlags.Public | BindingFlags.Instance);
                    if (ip?.GetValue(info) is int ie) return ie;
                }
            }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"[LootNet] XP read failed: {ex.Message}"); }
            return 0;
        }

        private static object _cachedSessionCounters;
        private static System.Reflection.MethodInfo _cachedGetAllInt;
        private static object _cachedExpTag;
        private static bool   _sessionCountersResolved;

        private static int TryReadSessionXp(Player player)
            => TryReadSessionXpFromProfile(player?.Profile);

        public static int TryReadCounterTag(string tagName)
        {
            try
            {
                object profile = _cachedProfileForPolling;
                if (profile == null)
                {
                    var mp = Singleton<GameWorld>.Instance?.MainPlayer;
                    profile = mp?.Profile;
                }
                if (profile == null) return 0;

                object counters = ResolveSessionCountersFromProfile(profile);
                if (counters == null || _cachedGetAllInt == null) return 0;

                object tagValue = ResolveCounterTagValue(tagName);
                if (tagValue == null) return 0;

                var result = _cachedGetAllInt.Invoke(counters, new object[] { new object[] { tagValue } });
                return ToInt(result);
            }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"[LootNet] TryReadCounterTag({tagName}) failed: {ex.Message}"); }
            return 0;
        }

        private static readonly Dictionary<string, object> _counterTagCache = new();
        private static object ResolveCounterTagValue(string name)
        {
            if (_counterTagCache.TryGetValue(name, out var cached)) return cached;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type ct = null;
                try { ct = Array.Find(asm.GetTypes(), t => t.Name == "CounterTag" && t.IsEnum); }
                catch { continue; }
                if (ct == null) continue;

                foreach (var n in Enum.GetNames(ct))
                {
                    if (n == name)
                    {
                        var val = Enum.Parse(ct, n);
                        _counterTagCache[name] = val;
                        return val;
                    }
                }
            }
            _counterTagCache[name] = null;
            return null;
        }

        private static int TryReadSessionXpFromProfile(object profile)
        {
            try
            {
                object counters = ResolveSessionCountersFromProfile(profile);
                if (counters == null) return 0;

                int viaApi = 0;
                if (_cachedGetAllInt != null && _cachedExpTag != null)
                {
                    var result = _cachedGetAllInt.Invoke(counters, new object[] { new object[] { _cachedExpTag } });
                    viaApi = ToInt(result);
                }

                if (viaApi > 0) return viaApi;
                return SumExpFromEnumerable(counters);
            }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"[LootNet] Session XP read failed: {ex.Message}"); }
            return 0;
        }

        private static bool _resolveDumped;
        private static object ResolveSessionCounters(Player player)
            => ResolveSessionCountersFromProfile(player?.Profile);

        private static object ResolveSessionCountersFromProfile(object profile)
        {
            if (profile == null)
            {
                if (!_resolveDumped) { _resolveDumped = true; Plugin.LogSource.LogWarning("[LootNet] Resolve: profile is null"); }
                return null;
            }
            var profType = profile.GetType();

            string[] statsNames = { "EftStats", "Stats", "ProfileStats", "_eftStats", "_stats" };
            object stats = null;
            foreach (var n in statsNames)
            {
                stats = profType.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(profile)
                     ?? profType.GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(profile);
                if (stats != null) break;
            }

            if (stats == null)
            {
                if (!_resolveDumped)
                {
                    _resolveDumped = true;
                    Plugin.LogSource.LogWarning($"[LootNet] Resolve: no stats field on {profType.FullName}. Members:");
                    foreach (var f in profType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        Plugin.LogSource.LogWarning($"  field: {f.FieldType.Name} {f.Name}");
                    foreach (var p in profType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        Plugin.LogSource.LogWarning($"  prop:  {p.PropertyType.Name} {p.Name}");
                }
                return null;
            }

            var statsType = stats.GetType();
            string[] counterNames = { "SessionCounters", "sessionCounters", "_sessionCounters", "Session" };
            object counters = null;
            foreach (var n in counterNames)
            {
                counters = statsType.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(stats)
                        ?? statsType.GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(stats);
                if (counters != null) break;
            }

            if (counters == null)
            {
                if (!_resolveDumped)
                {
                    _resolveDumped = true;
                    Plugin.LogSource.LogWarning($"[LootNet] Resolve: no SessionCounters on {statsType.FullName}. Members:");
                    foreach (var f in statsType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        Plugin.LogSource.LogWarning($"  field: {f.FieldType.Name} {f.Name}");
                    foreach (var p in statsType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        Plugin.LogSource.LogWarning($"  prop:  {p.PropertyType.Name} {p.Name}");
                }
                return null;
            }

            if (!_sessionCountersResolved)
            {
                _sessionCountersResolved = true;
                _cachedSessionCounters = counters;

                var countersType = counters.GetType();
                _cachedGetAllInt = countersType.GetMethod("GetAllInt", new[] { typeof(object[]) });

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type ct = null;
                    try { ct = Array.Find(asm.GetTypes(), t => t.Name == "CounterTag" && t.IsEnum); }
                    catch { continue; }
                    if (ct == null) continue;

                    foreach (var name in Enum.GetNames(ct))
                    {
                        if (name == "Exp") { _cachedExpTag = Enum.Parse(ct, name); break; }
                    }
                    if (_cachedExpTag != null) break;
                }

                if (_cachedGetAllInt == null || _cachedExpTag == null)
                    Plugin.LogSource.LogWarning($"[LootNet] SessionCounters API partially unresolved: GetAllInt={_cachedGetAllInt != null}, ExpTag={_cachedExpTag}");
            }

            return counters;
        }

        private static int SumExpFromEnumerable(object counters)
        {
            try
            {
                var enumerable = counters as System.Collections.IEnumerable;
                if (enumerable == null)
                {
                    var fld = counters.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    foreach (var f in fld)
                    {
                        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(f.FieldType))
                        {
                            enumerable = f.GetValue(counters) as System.Collections.IEnumerable;
                            if (enumerable != null) break;
                        }
                    }
                }
                if (enumerable == null) return 0;

                int total = 0;
                foreach (var item in enumerable)
                {
                    if (item == null) continue;
                    var itemType = item.GetType();

                    var keyObj   = itemType.GetField("Key")?.GetValue(item)   ?? itemType.GetProperty("Key")?.GetValue(item);
                    var valueObj = itemType.GetField("Value")?.GetValue(item) ?? itemType.GetProperty("Value")?.GetValue(item);
                    if (keyObj == null || valueObj == null) continue;

                    bool hasExp = false;
                    if (keyObj is System.Collections.IEnumerable keys)
                    {
                        foreach (var k in keys)
                        {
                            if (k != null && k.ToString() == "Exp") { hasExp = true; break; }
                        }
                    }
                    else if (keyObj.ToString().Contains("Exp")) hasExp = true;

                    if (hasExp) total += ToInt(valueObj);
                }
                return total;
            }
            catch { return 0; }
        }

        private static int ToInt(object v)
        {
            if (v == null) return 0;
            if (v is int i) return i;
            if (v is long l) return (int)l;
            if (v is double d) return (int)d;
            if (v is float f) return (int)f;
            try { return Convert.ToInt32(v); } catch { return 0; }
        }

        private static int ComputeXpEarned()
        {
            try
            {
                var mp = Singleton<GameWorld>.Instance?.MainPlayer;

                if (mp != null)
                {
                    int liveSession = TryReadSessionXp(mp);
                    if (liveSession > 0) return liveSession;
                }

                if (_latestSessionXp > 0) return _latestSessionXp;

                if (mp != null)
                {
                    int end = TryReadExperience(mp);
                    int delta = (end > 0 && _pendingStartXp > 0) ? end - _pendingStartXp : 0;
                    if (delta > 0) return delta;
                }
            }
            catch (Exception ex) { Plugin.LogSource.LogWarning($"[LootNet] ComputeXpEarned threw: {ex.Message}"); }
            return 0;
        }

        private static readonly Dictionary<string, string> _mapNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["bigmap"]        = "Customs",
            ["interchange"]   = "Interchange",
            ["woods"]         = "Woods",
            ["shoreline"]     = "Shoreline",
            ["rezervbase"]    = "Reserve",
            ["lighthouse"]    = "Lighthouse",
            ["tarkovstreets"] = "Streets",
            ["sandbox"]       = "Ground Zero",
            ["sandbox_high"]  = "Ground Zero (High)",
            ["laboratory"]    = "Labs",
            ["factory4_day"]  = "Factory (Day)",
            ["factory4_night"]= "Factory (Night)",
        };

        private static string FormatMapName(string raw)
        {
            if (_mapNames.TryGetValue(raw, out var friendly)) return friendly;

            return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(raw.ToLower());
        }

        private static void ResolvePit()
        {
            if (_pitResolved) return;
            _pitResolved = true;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type bossPlayers = null;
                try { bossPlayers = Array.Find(asm.GetTypes(), t => t.Name == "BossPlayers"); }
                catch { continue; }
                if (bossPlayers == null) continue;

                var isFollower = bossPlayers.GetMethod("IsFollowerProfileId", BindingFlags.Public | BindingFlags.Static);
                if (isFollower == null) continue;

                _pitIsFollower = isFollower;
                _isFollower    = (Func<string, bool>)Delegate.CreateDelegate(typeof(Func<string, bool>), isFollower);
                Plugin.LogSource.LogInfo("[LootNet] PIT Fireteam detected - fireteam stats enabled.");
                return;
            }

            Plugin.LogSource.LogInfo("[LootNet] PIT Fireteam not found - fireteam section disabled.");
        }
    }

    internal class RaidStartPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(GameWorld), "OnGameStarted");

        [PatchPostfix]
        private static void PatchPostfix()
        {
            var player = Singleton<GameWorld>.Instance?.MainPlayer as Player;
            RaidTracker.OnRaidEntered(player);

        }
    }

    internal static class DeathTracker
    {
        internal static void Postfix(Player __instance)
        {
            try
            {
                if (!RaidTracker.IsInRaid) return;
                if (__instance == null || !__instance.IsYourPlayer) return;
                RaidTracker.MarkPlayerDied();
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[LN Death] Exception: {ex.Message}");
            }
        }
    }

    internal static class KillTracker
    {

        internal static void Postfix(Player __instance, IPlayer __0, DamageInfoStruct __1)
        {
            try
            {
                var mainPlayer = Singleton<GameWorld>.Instance?.MainPlayer;
                if (mainPlayer == null) return;
                if (__instance.ProfileId == mainPlayer.ProfileId) return;

                IPlayer aggressor = __0 ?? __1.Player?.iPlayer;
                if (aggressor == null) return;

                if (!RaidTracker.MarkKillSeen(__instance.ProfileId)) return;

                bool isPmc = __instance.Side == EPlayerSide.Bear
                          || __instance.Side == EPlayerSide.Usec;

                bool isPlayerKill = aggressor.ProfileId == mainPlayer.ProfileId
                                 || (aggressor is Player p && p.IsYourPlayer);

                if (isPlayerKill)
                    RaidTracker.TrackKill(isPmc);
                else
                    RaidTracker.TrackBotKill(aggressor);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[LN Kill] Exception: {ex.Message}");
            }
        }
    }
}
