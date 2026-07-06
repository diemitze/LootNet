using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT.UI;
using LootNet.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LootNet.UI
{

    public class TeamSummaryDisplay : MonoBehaviour
    {
        private static TeamSummaryDisplay _instance;
        public static TeamSummaryDisplay Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("LootNetTeamSummary");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<TeamSummaryDisplay>();
                }
                return _instance;
            }
        }

        public event Action OnClosed;

        private const string LocalKey = "__local__";

        private static readonly Color Gold      = new Color(1f, 0.84f, 0f);
        private static readonly Color DimGold   = new Color(1f, 0.84f, 0f, 0.35f);
        private static readonly Color SurvivedC = new Color(0.40f, 1f, 0.45f);
        private static readonly Color DiedC     = new Color(1f, 0.32f, 0.32f);
        private static readonly Color LabelC    = new Color(0.52f, 0.52f, 0.58f);
        private static readonly Color RowBg     = new Color(0.075f, 0.085f, 0.105f, 0.92f);
        private static readonly Color RowBgTop  = new Color(0.115f, 0.105f, 0.055f, 0.95f);
        private static readonly Color ExpandBg  = new Color(0.045f, 0.05f, 0.07f, 0.92f);
        private static readonly Color XpC       = new Color(0.55f, 0.78f, 1f);

        private const float FadeInDuration = 0.35f;

        private const float FastRefreshInterval = 1.5f;
        private const float MaxPollInterval     = 15f;
        private const float PollBackoff         = 1.6f;
        private const float PollHardCap         = 180f;

        private const float BoardW    = 1120f;
        private const float RightPad  = 52f;
        private const float BarW      = 300f;

        private Canvas          _canvas;
        private CanvasGroup     _canvasGroup;
        private GameObject      _root;
        private RectTransform   _panel;
        private TextMeshProUGUI _titleText;
        private TextMeshProUGUI _subtitleText;
        private TextMeshProUGUI _heroValue;
        private RectTransform   _board;
        private Image           _refreshBg;
        private Image           _scanLine;
        private Texture2D       _vignetteTexture;
        private static Sprite   _circleSprite;

        private RaidStats _localStats;
        private readonly HashSet<string> _collapsedKeys = new();
        private bool      _visible;
        private float     _refreshTimer;
        private float     _pollInterval;
        private float     _openElapsed;
        private int       _lastVersion = -1;
        private bool      _built;

        private string  _displayedSig;
        private double  _displayedTotal;
        private Vector2 _heroHome;

        private readonly HashSet<string> _knownKeys = new();
        private bool _firstBuild = true;

        private readonly List<Coroutine> _animCos = new();

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
        }

        public void ShowForRaid(RaidStats localStats)
        {
            _localStats = localStats;
            if (_visible) { _lastVersion = -1; TeamSummaryStore.RequestRefresh(); return; }

            try { if (!_built) BuildUI(); }
            catch (Exception ex) { Plugin.LogSource.LogError($"[LootNet] TeamPanel BuildUI failed: {ex}"); return; }

            _collapsedKeys.Clear();
            _visible        = true;
            _lastVersion    = -1;
            _refreshTimer   = 0f;
            _pollInterval   = FastRefreshInterval;
            _openElapsed    = 0f;
            _displayedSig   = null;
            _displayedTotal = 0;
            _firstBuild     = true;
            _knownKeys.Clear();
            _root.SetActive(true);
            TeamSummaryStore.RequestRefresh();

            PlaySound("MenuButtonClick");
            StopAllCoroutines();
            _animCos.Clear();
            StartCoroutine(FadeIn());
        }

        public void Hide()
        {
            if (!_visible) return;
            _visible = false;
            StopAllCoroutines();
            StartCoroutine(FadeOut());
        }

        private void Update()
        {
            if (!_visible) return;

            if (TeamSummaryStore.Version != _lastVersion)
            {
                _lastVersion = TeamSummaryStore.Version;
                string sig = ComputeSig();
                if (sig != _displayedSig)
                {
                    Rebuild(animate: true);
                    _pollInterval = FastRefreshInterval;
                }
            }

            _openElapsed += Time.unscaledDeltaTime;

            int expected = RaidTracker.ExpectedTeammates?.Invoke() ?? 0;
            bool allIn   = expected > 0 && TeamSummaryStore.Count >= expected;
            if (allIn || _openElapsed >= PollHardCap) return;

            _refreshTimer += Time.unscaledDeltaTime;
            if (_refreshTimer >= _pollInterval)
            {
                _refreshTimer = 0f;
                _pollInterval = Mathf.Min(_pollInterval * PollBackoff, MaxPollInterval);
                TeamSummaryStore.RequestRefresh();
                PulseRefresh();
            }
        }

        private IEnumerator FadeIn()
        {
            _canvasGroup.alpha = 0f;
            Rebuild(animate: true);

            Vector2 home  = _panel != null ? _panel.anchoredPosition : Vector2.zero;
            Vector2 start = home + new Vector2(0f, -36f);
            if (_panel != null) _panel.anchoredPosition = start;

            float t = 0f;
            while (t < FadeInDuration)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.SmoothStep(0f, 1f, t / FadeInDuration);
                _canvasGroup.alpha = p;
                if (_panel != null) _panel.anchoredPosition = Vector2.Lerp(start, home, p);
                yield return null;
            }
            _canvasGroup.alpha = 1f;
            if (_panel != null) _panel.anchoredPosition = home;

            yield return StartCoroutine(AnimateScanLine());
        }

        private IEnumerator AnimateScanLine()
        {
            if (_scanLine == null) yield break;
            var rt = _scanLine.rectTransform;
            while (_visible)
            {
                float t = 0f;
                const float sweep = 3.5f;
                while (t < sweep && _visible)
                {
                    t += Time.unscaledDeltaTime;
                    float p = t / sweep;
                    rt.anchorMin = new Vector2(0f, 1f - p);
                    rt.anchorMax = new Vector2(1f, 1f - p);
                    _scanLine.color = new Color(1f, 1f, 1f, 0.022f * Mathf.Sin(p * Mathf.PI));
                    yield return null;
                }
                yield return new WaitForSecondsRealtime(2f);
            }
        }

        private void OnDestroy()
        {
            if (_vignetteTexture != null) Destroy(_vignetteTexture);
        }

        private IEnumerator FadeOut()
        {
            float start = _canvasGroup.alpha;
            float t = 0f;
            while (t < 0.3f)
            {
                t += Time.unscaledDeltaTime;
                _canvasGroup.alpha = Mathf.Lerp(start, 0f, t / 0.3f);
                yield return null;
            }
            _canvasGroup.alpha = 0f;
            if (_root != null) _root.SetActive(false);
            var cb = OnClosed; OnClosed = null;
            cb?.Invoke();
        }

        private struct Entry { public string Key; public string Name; public RaidStats Stats; public bool IsLocal; }

        private List<Entry> CollectEntries()
        {
            var list = new List<Entry>();
            if (_localStats != null)
                list.Add(new Entry { Key = LocalKey, Name = "You", Stats = _localStats, IsLocal = true });

            foreach (var kv in TeamSummaryStore.SnapshotPairs())
            {
                string name = string.IsNullOrEmpty(kv.Value.PlayerName) ? "Teammate" : kv.Value.PlayerName;
                list.Add(new Entry { Key = kv.Key, Name = name, Stats = kv.Value, IsLocal = false });
            }
            return list.OrderByDescending(e => e.Stats?.TotalFoundValue ?? 0).ToList();
        }

        private string ComputeSig()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var e in CollectEntries())
                sb.Append(e.Key).Append('§').Append(Signature(e.Stats)).Append('¶');
            return sb.ToString();
        }

        private static string Signature(RaidStats s)
        {
            if (s == null) return "∅";
            var sb = new System.Text.StringBuilder();
            sb.Append(s.TotalFoundValue).Append('|').Append(s.ItemsFound).Append('|')
              .Append(s.PmcKills).Append('|').Append(s.ScavKills).Append('|')
              .Append(s.XpEarned).Append('|').Append(s.XpBonus).Append('|')
              .Append(s.PlayerSurvived).Append('|').Append(s.PlayerName).Append('|').Append(s.MapName);
            if (s.TopItems != null)
                foreach (var it in s.TopItems) sb.Append('|').Append(it.Name).Append('=').Append(it.Value);
            return sb.ToString();
        }

        private void Rebuild(bool animate)
        {
            if (!_built) return;

            StopAnims();
            ClearChildren(_board);

            var entries = CollectEntries();
            _displayedSig = ComputeSig();

            string map = _localStats?.MapName;
            if (string.IsNullOrEmpty(map))
                map = entries.FirstOrDefault(e => !string.IsNullOrEmpty(e.Stats.MapName)).Stats?.MapName ?? "Raid";

            double total  = entries.Sum(e => e.Stats?.TotalFoundValue ?? 0);
            int    kills  = entries.Sum(e => (e.Stats?.PmcKills ?? 0) + (e.Stats?.ScavKills ?? 0));
            int    items  = entries.Sum(e => e.Stats?.ItemsFound ?? 0);

            int expected = RaidTracker.ExpectedTeammates?.Invoke() ?? 0;
            int missing  = Mathf.Max(0, expected - TeamSummaryStore.Count);

            _subtitleText.text = entries.Count <= 1 && missing > 0
                ? $"{map}  ·  waiting for teammates to extract…"
                : $"{map}  ·  {entries.Count} players  ·  {kills} kills  ·  {items} items looted";

            UpdateHero(total, animate);

            double maxVal = entries.Count > 0 ? entries.Max(e => e.Stats?.TotalFoundValue ?? 0) : 0;

            int expandedCount = entries.Count(e => e.Stats != null && !_collapsedKeys.Contains(e.Key));
            bool dense   = entries.Count >= 6 || expandedCount >= 3;
            float rowH   = dense ? 62f : 76f;
            int   maxFinds = expandedCount >= 3 ? 3 : 5;

            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                bool isNew = !_firstBuild && !_knownKeys.Contains(e.Key);
                bool expanded = e.Stats != null && !_collapsedKeys.Contains(e.Key);
                BuildRow(e, i, entries.Count, maxVal, rowH, animate, isNew, expanded);
                if (expanded)
                    BuildExpansion(e, animate, maxFinds);
                if (isNew) PlaySound("MenuDropdownSelect", "MenuCheckBox");
            }

            if (missing > 0)
                BuildGhostRow(missing, rowH);

            _firstBuild = false;
            _knownKeys.Clear();
            foreach (var e in entries) _knownKeys.Add(e.Key);
        }

        private void UpdateHero(double total, bool animate)
        {
            if (_heroValue == null) return;
            double from = _displayedTotal;
            _displayedTotal = total;

            if (animate && Math.Abs(total - from) > 0.5)
            {
                StartAnim(HeroRise(from, total));
            }
            else
            {
                _heroValue.rectTransform.anchoredPosition = _heroHome;
                _heroValue.rectTransform.localScale = Vector3.one;
                _heroValue.color = ValueColor(total);
                _heroValue.text  = $"₽ {total:N0}";
            }
        }

        private IEnumerator HeroRise(double from, double target)
        {
            if (_heroValue == null) yield break;
            var rt = _heroValue.rectTransform;
            rt.localScale = Vector3.one;
            Color end = ValueColor(target);
            Color dim = new Color(end.r, end.g, end.b, 0.5f);

            float dur = 1f, t = 0f;
            while (t < dur)
            {
                if (_heroValue == null) yield break;
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / dur);
                float eased = 1f - Mathf.Pow(1f - p, 3f);
                double v = from + (target - from) * eased;
                _heroValue.text = $"₽ {v:N0}";
                rt.anchoredPosition = _heroHome + new Vector2(0f, (1f - eased) * -12f);
                _heroValue.color = Color.Lerp(dim, end, eased);
                yield return null;
            }
            _heroValue.text = $"₽ {target:N0}";
            rt.anchoredPosition = _heroHome;
            _heroValue.color = end;
            yield return PulseScale(rt, 1.08f, 0.28f);
            if (rt != null) rt.localScale = Vector3.one;
        }

        private static IEnumerator PulseScale(RectTransform rt, float peak, float dur)
        {
            float t = 0f;
            while (t < dur)
            {
                if (rt == null) yield break;
                t += Time.unscaledDeltaTime;
                float p = Mathf.Sin(Mathf.Clamp01(t / dur) * Mathf.PI);
                rt.localScale = Vector3.one * Mathf.Lerp(1f, peak, p);
                yield return null;
            }
            if (rt != null) rt.localScale = Vector3.one;
        }

        private void BuildRow(Entry e, int rank, int count, double maxVal, float rowH, bool animate, bool isNew, bool expanded)
        {
            var s = e.Stats;
            bool alive    = s?.PlayerSurvived ?? false;
            bool topRow   = rank == 0 && count > 1;

            float yNum = rowH >= 70f ? -12f : -8f;
            float yLbl = rowH >= 70f ? -40f : -34f;

            var row = MakeRect("Row", _board);
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = rowH; le.minHeight = rowH;
            var bg = row.AddComponent<Image>();
            bg.color = topRow ? RowBgTop : RowBg;
            var cg = row.AddComponent<CanvasGroup>();

            MakeBar("u", row.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), expanded ? 2f : 1f,
                    expanded ? Gold : new Color(1f, 1f, 1f, 0.05f));

            if (topRow)
            {
                var stripe = MakeRect("TopStripe", row.transform);
                var sRt = stripe.GetComponent<RectTransform>();
                sRt.anchorMin = new Vector2(0f, 0f); sRt.anchorMax = new Vector2(0f, 1f);
                sRt.pivot = new Vector2(0f, 0.5f);
                sRt.anchoredPosition = Vector2.zero; sRt.sizeDelta = new Vector2(3f, 0f);
                stripe.AddComponent<Image>().color = Gold;
            }

            var chip = MakeRect("Rank", row.transform);
            var chRt = chip.GetComponent<RectTransform>();
            chRt.anchorMin = chRt.anchorMax = new Vector2(0f, 0.5f); chRt.pivot = new Vector2(0f, 0.5f);
            chRt.anchoredPosition = new Vector2(14f, 0f); chRt.sizeDelta = new Vector2(36f, 20f);
            chip.AddComponent<Image>().color = RankColor(rank);
            var chL = MakeTMP("L", chip.transform, 11f, FontStyles.Bold, TextAlignmentOptions.Center);
            chL.color = new Color(0.05f, 0.05f, 0.05f); chL.text = RankLabel(rank);
            Stretch(chL.rectTransform);

            var av = MakeRect("Avatar", row.transform);
            var avRt = av.GetComponent<RectTransform>();
            avRt.anchorMin = avRt.anchorMax = new Vector2(0f, 0.5f); avRt.pivot = new Vector2(0f, 0.5f);
            avRt.anchoredPosition = new Vector2(60f, 0f); avRt.sizeDelta = new Vector2(rowH >= 70f ? 42f : 36f, rowH >= 70f ? 42f : 36f);
            var avImg = av.AddComponent<Image>();
            avImg.sprite = CircleSprite(); avImg.type = Image.Type.Simple;
            avImg.color = AvatarColor(e.Name, e.IsLocal);
            var ini = MakeTMP("Ini", av.transform, 17f, FontStyles.Bold, TextAlignmentOptions.Center);
            ini.color = new Color(0.06f, 0.06f, 0.08f); ini.text = Initials(e.Name);
            Stretch(ini.rectTransform);

            var name = MakeTMP("Name", row.transform, 18f, FontStyles.Bold, TextAlignmentOptions.Left);
            name.color = e.IsLocal ? Gold : new Color(0.92f, 0.92f, 0.94f);
            name.enableWordWrapping = false; name.overflowMode = TextOverflowModes.Ellipsis;
            name.text = e.Name;
            Place(name.rectTransform, 0f, 1f, 112f, yNum, 250f, 24f);

            var status = MakeTMP("Status", row.transform, 11f, FontStyles.Bold, TextAlignmentOptions.Left);
            status.color = alive ? SurvivedC : DiedC;
            status.characterSpacing = 1f;
            status.text = alive ? "● SURVIVED" : "● KIA";
            Place(status.rectTransform, 0f, 1f, 113f, yLbl, 250f, 15f);

            AddStatCol(row.transform, 392f, s?.PmcKills.ToString() ?? "0",
                       (s?.PmcKills ?? 0) > 0 ? new Color(1f, 0.5f, 0.5f) : Color.white, "PMC", yNum, yLbl);
            AddStatCol(row.transform, 470f, s?.ScavKills.ToString() ?? "0", Color.white, "SCAV", yNum, yLbl);
            AddStatCol(row.transform, 548f, s?.ItemsFound.ToString() ?? "0", Color.white, "ITEMS", yNum, yLbl);
            AddStatCol(row.transform, 634f, ShortNum((s?.XpEarned ?? 0) + (s?.XpBonus ?? 0)),
                       (s?.XpEarned ?? 0) > 0 ? XpC : Color.white, "XP", yNum, yLbl);

            double v = s?.TotalFoundValue ?? 0;
            var val = MakeTMP("Haul", row.transform, rowH >= 70f ? 22f : 19f, FontStyles.Bold, TextAlignmentOptions.Right);
            val.color = ValueColor(v);
            val.text  = $"₽ {v:N0}";
            var vRt = val.rectTransform;
            vRt.anchorMin = vRt.anchorMax = new Vector2(1f, 1f); vRt.pivot = new Vector2(1f, 1f);
            vRt.anchoredPosition = new Vector2(-RightPad, yNum + 2f); vRt.sizeDelta = new Vector2(260f, 28f);

            var track = MakeRect("BarTrack", row.transform);
            var tRt = track.GetComponent<RectTransform>();
            tRt.anchorMin = tRt.anchorMax = new Vector2(1f, 1f); tRt.pivot = new Vector2(1f, 1f);
            tRt.anchoredPosition = new Vector2(-RightPad, yLbl - 2f); tRt.sizeDelta = new Vector2(BarW, 5f);
            track.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.06f);

            float frac = maxVal > 0 ? Mathf.Clamp01((float)(v / maxVal)) : 0f;
            if (frac > 0f) frac = Mathf.Max(frac, 0.02f);
            var fill = MakeRect("Fill", track.transform);
            var fRt = fill.GetComponent<RectTransform>();
            fRt.anchorMin = Vector2.zero; fRt.anchorMax = new Vector2(frac, 1f);
            fRt.sizeDelta = Vector2.zero; fRt.anchoredPosition = Vector2.zero;
            Color fc = ValueColor(v);
            fill.AddComponent<Image>().color = new Color(fc.r, fc.g, fc.b, 0.85f);
            if (animate) StartAnim(AnimateBarFill(fRt, frac, 0.55f, 0.15f + rank * 0.06f));

            var chev = MakeTMP("Chev", row.transform, 12f, FontStyles.Normal, TextAlignmentOptions.Center);
            chev.color = expanded ? Gold : new Color(0.5f, 0.5f, 0.55f);
            chev.text  = expanded ? "▲" : "▼";
            var cvRt = chev.rectTransform;
            cvRt.anchorMin = cvRt.anchorMax = new Vector2(1f, 0.5f); cvRt.pivot = new Vector2(1f, 0.5f);
            cvRt.anchoredPosition = new Vector2(-16f, 0f); cvRt.sizeDelta = new Vector2(24f, 20f);

            string key = e.Key;
            var btn = row.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() =>
            {
                if (!_collapsedKeys.Remove(key)) _collapsedKeys.Add(key);
                PlaySound("ButtonClick", "MenuButtonClick");
                Rebuild(animate: false);
            });

            if (isNew)
                StartAnim(FlashNewRow(row.transform, cg, bg, bg.color));
            else if (animate && _firstBuild)
                StartAnim(RowIntro(cg, row.transform, rank * 0.06f));
        }

        private void AddStatCol(Transform row, float x, string num, Color numColor, string label, float yNum, float yLbl)
        {
            var n = MakeTMP("Num", row, 19f, FontStyles.Bold, TextAlignmentOptions.Left);
            n.color = numColor; n.text = num;
            n.enableWordWrapping = false;
            Place(n.rectTransform, 0f, 1f, x, yNum, 76f, 24f);

            var l = MakeTMP("Lbl", row, 9.5f, FontStyles.Normal, TextAlignmentOptions.Left);
            l.color = LabelC; l.characterSpacing = 2f; l.text = label;
            Place(l.rectTransform, 0f, 1f, x + 1f, yLbl, 76f, 13f);
        }

        private void BuildExpansion(Entry e, bool animate, int maxFinds)
        {
            var s = e.Stats;
            int rows = Math.Min(s.TopItems?.Count ?? 0, maxFinds);
            float h = 36f + Math.Max(rows, 1) * 32f + 10f;

            var exp = MakeRect("Expand", _board);
            var le = exp.AddComponent<LayoutElement>();
            le.preferredHeight = h; le.minHeight = h;
            exp.AddComponent<Image>().color = ExpandBg;
            var cg = exp.AddComponent<CanvasGroup>();

            var stripe = MakeRect("Stripe", exp.transform);
            var sRt = stripe.GetComponent<RectTransform>();
            sRt.anchorMin = new Vector2(0f, 0f); sRt.anchorMax = new Vector2(0f, 1f);
            sRt.pivot = new Vector2(0f, 0.5f);
            sRt.anchoredPosition = Vector2.zero; sRt.sizeDelta = new Vector2(2f, 0f);
            stripe.AddComponent<Image>().color = DimGold;

            var hdr = MakeTMP("Hdr", exp.transform, 11f, FontStyles.Bold, TextAlignmentOptions.Left);
            hdr.color = Gold; hdr.characterSpacing = 3f;
            hdr.text = "TOP FINDS";
            Place(hdr.rectTransform, 0f, 1f, 112f, -10f, 300f, 16f);

            if (s.XpEarned > 0)
            {
                var xp = MakeTMP("Xp", exp.transform, 12f, FontStyles.Bold, TextAlignmentOptions.Right);
                xp.color = XpC;
                xp.text  = s.XpBonus > 0
                    ? $"+{s.XpEarned:N0} XP  ·  +{s.XpBonus:N0} survival bonus"
                    : $"+{s.XpEarned:N0} XP";
                var xRt = xp.rectTransform;
                xRt.anchorMin = xRt.anchorMax = new Vector2(1f, 1f); xRt.pivot = new Vector2(1f, 1f);
                xRt.anchoredPosition = new Vector2(-RightPad, -9f); xRt.sizeDelta = new Vector2(420f, 16f);
            }

            if (rows == 0)
            {
                var none = MakeTMP("NoFinds", exp.transform, 13f, FontStyles.Italic, TextAlignmentOptions.Left);
                none.color = new Color(0.45f, 0.45f, 0.5f);
                none.text  = "No notable loot.";
                Place(none.rectTransform, 0f, 1f, 112f, -38f, 400f, 20f);
            }

            for (int i = 0; i < rows; i++)
            {
                var (itemName, itemVal) = s.TopItems[i];
                BuildFindRow(exp.transform, -36f - i * 32f, i, itemName, itemVal);
            }

            StartAnim(FadeCanvas(cg, 0.18f));
        }

        private void BuildFindRow(Transform parent, float y, int rank, string itemName, double itemVal)
        {
            var go = MakeRect("Find", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(110f, y);
            rt.sizeDelta = new Vector2(-(110f + RightPad), 30f);

            go.AddComponent<Image>().color = new Color(0.09f, 0.10f, 0.13f, (rank % 2 == 0) ? 0.55f : 0.30f);

            var acc = MakeRect("Accent", go.transform);
            var aRt = acc.GetComponent<RectTransform>();
            aRt.anchorMin = Vector2.zero; aRt.anchorMax = new Vector2(0f, 1f); aRt.pivot = new Vector2(0f, 0.5f);
            aRt.anchoredPosition = Vector2.zero; aRt.sizeDelta = new Vector2(3f, 0f);
            acc.AddComponent<Image>().color = RarityColor(itemVal);

            var badge = MakeRect("Badge", go.transform);
            var bRt = badge.GetComponent<RectTransform>();
            bRt.anchorMin = bRt.anchorMax = new Vector2(0f, 0.5f); bRt.pivot = new Vector2(0f, 0.5f);
            bRt.anchoredPosition = new Vector2(10f, 0f); bRt.sizeDelta = new Vector2(30f, 16f);
            badge.AddComponent<Image>().color = RankColor(rank);
            var bl = MakeTMP("BL", badge.transform, 10f, FontStyles.Bold, TextAlignmentOptions.Center);
            bl.color = new Color(0.05f, 0.05f, 0.05f); bl.text = RankLabel(rank);
            Stretch(bl.rectTransform);

            var nm = MakeTMP("FindName", go.transform, 13f, FontStyles.Normal, TextAlignmentOptions.Left);
            nm.color = new Color(0.88f, 0.88f, 0.9f);
            nm.enableWordWrapping = false; nm.overflowMode = TextOverflowModes.Ellipsis;
            nm.text = itemName;
            var nr = nm.rectTransform;
            nr.anchorMin = new Vector2(0f, 0f); nr.anchorMax = new Vector2(1f, 1f); nr.pivot = new Vector2(0f, 0.5f);
            nr.offsetMin = new Vector2(50f, 0f); nr.offsetMax = new Vector2(-96f, 0f);

            var vl = MakeTMP("FindVal", go.transform, 13f, FontStyles.Bold, TextAlignmentOptions.Right);
            vl.color = ValueColor(itemVal); vl.text = ShortValue(itemVal);
            var vr = vl.rectTransform;
            vr.anchorMin = new Vector2(1f, 0f); vr.anchorMax = new Vector2(1f, 1f); vr.pivot = new Vector2(1f, 0.5f);
            vr.anchoredPosition = new Vector2(-8f, 0f); vr.sizeDelta = new Vector2(84f, 0f);
        }

        private void BuildGhostRow(int missing, float rowH)
        {
            var row = MakeRect("Ghost", _board);
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = rowH; le.minHeight = rowH;
            row.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.02f);
            var cg = row.AddComponent<CanvasGroup>();

            var txt = MakeTMP("T", row.transform, 14f, FontStyles.Italic, TextAlignmentOptions.Center);
            txt.color = new Color(0.55f, 0.55f, 0.6f);
            txt.text  = missing == 1
                ? "waiting for 1 teammate to extract…"
                : $"waiting for {missing} teammates to extract…";
            Stretch(txt.rectTransform);

            StartAnim(PulseGhost(cg));
        }

        private void StartAnim(IEnumerator co) => _animCos.Add(StartCoroutine(co));

        private void StopAnims()
        {
            foreach (var c in _animCos) if (c != null) StopCoroutine(c);
            _animCos.Clear();
        }

        private static IEnumerator PulseGhost(CanvasGroup cg)
        {
            float t = 0f;
            while (cg != null)
            {
                t += Time.unscaledDeltaTime;
                cg.alpha = 0.5f + 0.25f * Mathf.Sin(t * 2.4f);
                yield return null;
            }
        }

        private static IEnumerator RowIntro(CanvasGroup cg, Transform tr, float delay)
        {
            if (cg == null) yield break;
            cg.alpha = 0f;
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
            float t = 0f;
            const float dur = 0.25f;
            while (t < dur)
            {
                if (cg == null) yield break;
                t += Time.unscaledDeltaTime;
                float p = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f);
                cg.alpha = p;
                if (tr != null) tr.localScale = new Vector3(1f, Mathf.Lerp(0.92f, 1f, p), 1f);
                yield return null;
            }
            cg.alpha = 1f;
            if (tr != null) tr.localScale = Vector3.one;
        }

        private static IEnumerator FlashNewRow(Transform tr, CanvasGroup cg, Image bg, Color baseColor)
        {
            Color flash = new Color(0.28f, 0.25f, 0.09f, 0.98f);
            float t = 0f;
            const float dur = 0.34f;
            while (t < dur)
            {
                if (tr == null) yield break;
                t += Time.unscaledDeltaTime;
                float p = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f);
                tr.localScale = new Vector3(1f, Mathf.Lerp(0.85f, 1f, p), 1f);
                if (cg != null) cg.alpha = Mathf.Clamp01(t / dur * 2f);
                if (bg != null) bg.color = Color.Lerp(flash, baseColor, p);
                yield return null;
            }
            if (tr != null) tr.localScale = Vector3.one;
            if (cg != null) cg.alpha = 1f;
            if (bg != null) bg.color = baseColor;
        }

        private static IEnumerator AnimateBarFill(RectTransform fill, float frac, float dur, float delay)
        {
            if (fill == null) yield break;
            fill.anchorMax = new Vector2(0f, 1f);
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
            float t = 0f;
            while (t < dur)
            {
                if (fill == null) yield break;
                t += Time.unscaledDeltaTime;
                float p = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f);
                fill.anchorMax = new Vector2(frac * p, 1f);
                yield return null;
            }
            if (fill != null) fill.anchorMax = new Vector2(frac, 1f);
        }

        private static IEnumerator FadeCanvas(CanvasGroup cg, float dur)
        {
            if (cg == null) yield break;
            float t = 0f;
            while (t < dur)
            {
                if (cg == null) yield break;
                t += Time.unscaledDeltaTime;
                cg.alpha = Mathf.Clamp01(t / dur);
                yield return null;
            }
            cg.alpha = 1f;
        }

        private void BuildUI()
        {
            _built = true;

            _canvas = gameObject.GetComponent<Canvas>() ?? gameObject.AddComponent<Canvas>();
            _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 501;
            if (!gameObject.GetComponent<CanvasScaler>())     gameObject.AddComponent<CanvasScaler>();
            if (!gameObject.GetComponent<GraphicRaycaster>()) gameObject.AddComponent<GraphicRaycaster>();
            _canvasGroup = gameObject.GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

            _root = MakeRect("TeamRoot", transform);
            Stretch(_root.GetComponent<RectTransform>());

            var overlay = MakeRect("Overlay", _root.transform);
            Stretch(overlay.GetComponent<RectTransform>());
            overlay.AddComponent<Image>().color = new Color(0.01f, 0.01f, 0.03f, 0.93f);

            _vignetteTexture = BuildVignetteTexture();
            var vig = MakeRect("Vignette", _root.transform);
            Stretch(vig.GetComponent<RectTransform>());
            var vigImg = vig.AddComponent<RawImage>();
            vigImg.texture = _vignetteTexture;
            vigImg.color = new Color(1f, 1f, 1f, 0.9f);
            vigImg.raycastTarget = false;

            var scanGo = MakeRect("ScanLine", _root.transform);
            var scanRt = scanGo.GetComponent<RectTransform>();
            scanRt.anchorMin = new Vector2(0f, 1f); scanRt.anchorMax = new Vector2(1f, 1f);
            scanRt.pivot = Vector2.up; scanRt.sizeDelta = new Vector2(0f, 2.5f);
            _scanLine = scanGo.AddComponent<Image>();
            _scanLine.color = Color.clear;
            _scanLine.raycastTarget = false;

            var click = MakeRect("ClickCatcher", _root.transform);
            Stretch(click.GetComponent<RectTransform>());
            click.AddComponent<Image>().color = Color.clear;
            var btn = click.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(Hide);

            MakeBar("TopAccent", _root.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), 4f, Gold);
            MakeBar("BotAccent", _root.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), 2f, DimGold);

            _titleText = MakeTMP("Title", _root.transform, 42f, FontStyles.Bold, TextAlignmentOptions.Center);
            _titleText.color = Gold;
            _titleText.characterSpacing = 6f;
            _titleText.text  = "TEAM SUMMARY";
            Place(_titleText.rectTransform, 0.5f, 1f, 0f, -46f, 1200f, 52f);

            _subtitleText = MakeTMP("Subtitle", _root.transform, 17f, FontStyles.Normal, TextAlignmentOptions.Center);
            _subtitleText.color = new Color(0.7f, 0.7f, 0.75f);
            Place(_subtitleText.rectTransform, 0.5f, 1f, 0f, -96f, 1200f, 26f);

            var panelGo = MakeRect("Panel", _root.transform);
            _panel = panelGo.GetComponent<RectTransform>();
            Stretch(_panel);

            _heroValue = MakeTMP("HeroValue", panelGo.transform, 44f, FontStyles.Bold, TextAlignmentOptions.Center);
            _heroValue.color = Gold;
            _heroValue.text  = "₽ 0";
            Place(_heroValue.rectTransform, 0.5f, 1f, 0f, -136f, 800f, 52f);
            _heroHome = _heroValue.rectTransform.anchoredPosition;

            var heroLabel = MakeTMP("HeroLabel", panelGo.transform, 11f, FontStyles.Normal, TextAlignmentOptions.Center);
            heroLabel.color = LabelC; heroLabel.characterSpacing = 4f;
            heroLabel.text  = "TEAM HAUL";
            Place(heroLabel.rectTransform, 0.5f, 1f, 0f, -188f, 400f, 16f);

            var boardGo = MakeRect("Board", panelGo.transform);
            _board = boardGo.GetComponent<RectTransform>();
            _board.anchorMin = _board.anchorMax = new Vector2(0.5f, 1f);
            _board.pivot = new Vector2(0.5f, 1f);
            _board.anchoredPosition = new Vector2(0f, -224f);
            _board.sizeDelta = new Vector2(BoardW, 0f);
            var vlg = boardGo.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6f; vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            var fit = boardGo.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var refreshGo = MakeRect("RefreshBtn", _root.transform);
            var rbRt = refreshGo.GetComponent<RectTransform>();
            rbRt.anchorMin = rbRt.anchorMax = new Vector2(1f, 1f); rbRt.pivot = new Vector2(1f, 1f);
            rbRt.anchoredPosition = new Vector2(-30f, -30f); rbRt.sizeDelta = new Vector2(130f, 36f);
            _refreshBg = refreshGo.AddComponent<Image>();
            _refreshBg.color = new Color(0.16f, 0.17f, 0.20f, 0.95f);
            var rbBtn = refreshGo.AddComponent<Button>();
            rbBtn.transition = Selectable.Transition.None;
            rbBtn.onClick.AddListener(() => { _pollInterval = FastRefreshInterval; _refreshTimer = 0f; TeamSummaryStore.RequestRefresh(); PulseRefresh(); PlaySound("ButtonClick", "MenuButtonClick"); });

            var rbLabel = MakeTMP("RefreshLabel", refreshGo.transform, 16f, FontStyles.Bold, TextAlignmentOptions.Center);
            rbLabel.color = Gold; rbLabel.text = "Refresh";
            Stretch(rbLabel.rectTransform);

            var hint = MakeTMP("Hint", _root.transform, 16f, FontStyles.Italic, TextAlignmentOptions.Center);
            hint.color = new Color(0.5f, 0.5f, 0.55f);
            hint.text  = "Click a player to collapse their finds  ·  click anywhere else to close";
            Place(hint.rectTransform, 0.5f, 0f, 0f, 24f, 800f, 24f);
        }

        private static void ClearChildren(RectTransform rt)
        {
            for (int i = rt.childCount - 1; i >= 0; i--)
            {
                var child = rt.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
        }

        private static string ShortValue(double v)
        {
            if (v >= 1_000_000) return $"₽{v / 1_000_000:0.0}M";
            if (v >= 1_000)     return $"₽{v / 1_000:0}k";
            return $"₽{v:0}";
        }

        private static string ShortNum(int v)
        {
            if (v >= 10_000) return $"{v / 1000f:0.#}k";
            return v.ToString("N0");
        }

        private static Color RankColor(int i) => i switch
        {
            0 => new Color(1f,    0.84f, 0f,    1f),
            1 => new Color(0.75f, 0.75f, 0.75f, 1f),
            2 => new Color(0.80f, 0.50f, 0.20f, 1f),
            _ => new Color(0.55f, 0.55f, 0.6f,  1f),
        };

        private static Color ValueColor(double v)
        {
            if (v >= 1_000_000) return new Color(1f,    0.20f, 0.20f);
            if (v >= 500_000)   return new Color(1f,    0f,    1f);
            if (v >= 300_000)   return new Color(1f,    0.40f, 0.80f);
            if (v >= 150_000)   return new Color(0.40f, 0.80f, 1f);
            if (v >= 50_000)    return new Color(0.40f, 1f,    0.40f);
            return Color.white;
        }

        private static string RankLabel(int i) => $"#{i + 1}";

        private static Color RarityColor(double v)
        {
            if (v >= 1_000_000) return new Color(1f,    0.15f, 0.15f);
            if (v >= 500_000)   return new Color(1f,    0.20f, 0.80f);
            if (v >= 300_000)   return new Color(0.75f, 0.30f, 1f);
            if (v >= 150_000)   return new Color(0.20f, 0.70f, 1f);
            if (v >= 50_000)    return new Color(1f,    0.85f, 0.10f);
            return new Color(0.40f, 0.40f, 0.40f);
        }

        private static readonly Color[] AvatarPalette =
        {
            new Color(0.45f, 0.70f, 0.95f), new Color(0.55f, 0.85f, 0.60f),
            new Color(0.90f, 0.60f, 0.45f), new Color(0.80f, 0.60f, 0.95f),
            new Color(0.95f, 0.75f, 0.45f), new Color(0.55f, 0.85f, 0.85f),
        };

        private static Color AvatarColor(string name, bool local)
        {
            if (local) return Gold;
            int h = Mathf.Abs((name ?? "").GetHashCode());
            return AvatarPalette[h % AvatarPalette.Length];
        }

        private static string Initials(string n)
        {
            n = (n ?? "").Trim();
            return n.Length == 0 ? "?" : n.Substring(0, 1).ToUpperInvariant();
        }

        private static Sprite CircleSprite()
        {
            if (_circleSprite != null) return _circleSprite;
            const int s = 64;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[s * s];
            float r = s / 2f - 1f, c = (s - 1) / 2f;
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = x - c, dy = y - c;
                float a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy));
                px[y * s + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px); tex.Apply();
            _circleSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f));
            return _circleSprite;
        }

        private static Texture2D BuildVignetteTexture()
        {
            const int s = 64;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode   = TextureWrapMode.Clamp,
            };
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = (x / (s - 1f)) * 2f - 1f;
                float dy = (y / (s - 1f)) * 2f - 1f;
                float d  = Mathf.Sqrt(dx * dx + dy * dy);
                float a  = Mathf.Clamp01((d - 0.5f) / 0.65f);
                a        = a * a;
                px[y * s + x] = new Color32(0, 0, 0, (byte)(a * 210f));
            }
            tex.SetPixels32(px); tex.Apply();
            return tex;
        }

        private static void Place(RectTransform r, float ax, float ay, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(ax, ay);
            r.pivot = new Vector2(ax, ay);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(w, h);
        }

        private static GameObject MakeRect(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            return go;
        }

        private static TextMeshProUGUI MakeTMP(string name, Transform parent, float size, FontStyles style, TextAlignmentOptions align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.fontSize = size; t.fontStyle = style; t.alignment = align; t.richText = true;
            return t;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero; rt.anchoredPosition = Vector2.zero;
        }

        private static RectTransform MakeBar(string name, Transform parent, Vector2 aMin, Vector2 aMax, float thickness, Color color)
        {
            var go = MakeRect(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.pivot = new Vector2(0.5f, aMin.y);
            rt.sizeDelta = new Vector2(0f, thickness);
            rt.anchoredPosition = Vector2.zero;
            go.AddComponent<Image>().color = color;
            return rt;
        }

        private void PulseRefresh()
        {
            if (_refreshBg == null) return;
            StartCoroutine(PulseRefreshCo());
        }

        private IEnumerator PulseRefreshCo()
        {
            Color baseC = new Color(0.16f, 0.17f, 0.20f, 0.95f);
            Color hot   = new Color(0.30f, 0.28f, 0.12f, 0.98f);
            float dur = 0.4f, t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Sin(Mathf.Clamp01(t / dur) * Mathf.PI);
                if (_refreshBg != null) _refreshBg.color = Color.Lerp(baseC, hot, p);
                yield return null;
            }
            if (_refreshBg != null) _refreshBg.color = baseC;
        }

        private static void PlaySound(string soundName, string fallback = null)
        {
            try
            {
                var gs = Singleton<GUISounds>.Instance;
                if (gs == null) return;
                if (Enum.TryParse(soundName, out EUISoundType s))  { gs.PlayUISound(s); return; }
                if (fallback != null && Enum.TryParse(fallback, out EUISoundType f)) gs.PlayUISound(f);
            }
            catch { }
        }
    }
}
