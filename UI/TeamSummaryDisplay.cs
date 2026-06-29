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

        private static readonly Color Gold     = new Color(1f, 0.84f, 0f);
        private static readonly Color DimGold   = new Color(1f, 0.84f, 0f, 0.35f);
        private static readonly Color SurvivedC = new Color(0.40f, 1f, 0.45f);
        private static readonly Color DiedC     = new Color(1f, 0.32f, 0.32f);

        private const float FadeInDuration  = 0.35f;

        private const float FastRefreshInterval = 1.5f;
        private const float MaxPollInterval     = 15f;
        private const float PollBackoff         = 1.6f;
        private const float PollHardCap         = 180f;
        private const float CardW = 820f;
        private const float CardH = 452f;

        private Canvas          _canvas;
        private CanvasGroup     _canvasGroup;
        private GameObject      _root;
        private RectTransform   _panel;
        private TextMeshProUGUI _titleText;
        private TextMeshProUGUI _subtitleText;
        private RectTransform   _tabBar;
        private RectTransform   _contentCard;
        private CanvasGroup     _contentCg;
        private Image           _refreshBg;
        private Image           _scanLine;
        private Texture2D       _vignetteTexture;
        private static Sprite   _circleSprite;

        private RaidStats _localStats;
        private string    _selectedKey = LocalKey;
        private bool      _visible;
        private float     _refreshTimer;
        private float     _pollInterval;
        private float     _openElapsed;
        private int       _lastVersion = -1;
        private bool      _built;

        private string _displayedKey;
        private string _displayedSig;

        private readonly HashSet<string> _knownTabKeys = new();
        private bool _firstTabBuild = true;

        private struct PageAnim { public CanvasGroup Cg; public RectTransform Rt; public Vector2 From; }
        private readonly List<PageAnim> _pageAnims = new();
        private readonly List<Coroutine> _pageCos = new();
        private TextMeshProUGUI _pageValueText;
        private double          _pageValueTarget;
        private Color           _pageValueColor;

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

            _selectedKey   = LocalKey;
            _visible       = true;
            _lastVersion   = -1;
            _refreshTimer  = 0f;
            _pollInterval  = FastRefreshInterval;
            _openElapsed   = 0f;
            _displayedKey  = null;
            _displayedSig  = null;
            _firstTabBuild = true;
            _knownTabKeys.Clear();
            _root.SetActive(true);
            TeamSummaryStore.RequestRefresh();

            PlaySound("MenuButtonClick");
            StopAllCoroutines();
            _pageCos.Clear();
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
                Rebuild(animateContent: true);
                _pollInterval = FastRefreshInterval;
            }

            _openElapsed  += Time.unscaledDeltaTime;

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
            Rebuild(animateContent: true);

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

            foreach (var kv in TeamSummaryStore.SnapshotPairs().OrderByDescending(p => p.Value.TotalFoundValue))
            {
                string name = string.IsNullOrEmpty(kv.Value.PlayerName) ? "Teammate" : kv.Value.PlayerName;
                list.Add(new Entry { Key = kv.Key, Name = name, Stats = kv.Value, IsLocal = false });
            }
            return list;
        }

        private void Rebuild(bool animateContent)
        {
            if (!_built) return;

            var entries = CollectEntries();

            string map = _localStats?.MapName;
            if (string.IsNullOrEmpty(map))
                map = entries.FirstOrDefault(e => !string.IsNullOrEmpty(e.Stats.MapName)).Stats?.MapName ?? "Raid";

            int teammates = entries.Count - (_localStats != null ? 1 : 0);
            double teamTotal = entries.Sum(e => e.Stats?.TotalFoundValue ?? 0);
            _subtitleText.text = teammates <= 0
                ? $"{map}  ·  waiting for teammates to extract…"
                : $"{map}  ·  {entries.Count} players  ·  team haul ₽{teamTotal:N0}";

            if (!entries.Any(e => e.Key == _selectedKey))
                _selectedKey = entries.Count > 0 ? entries[0].Key : LocalKey;

            BuildTabs(entries);

            var sel = entries.FirstOrDefault(e => e.Key == _selectedKey);
            string sig = Signature(sel.Stats);

            bool selChanged = _selectedKey != _displayedKey || sig != _displayedSig;
            if (sel.Stats != null && selChanged)
            {
                StopPageAnims();
                ClearChildren(_contentCard);
                BuildPage(sel);
                _displayedKey = _selectedKey;
                _displayedSig = sig;
                if (animateContent) StartPage(AnimatePage());
                else               ShowPageInstant();
            }
            else if (sel.Stats == null)
            {
                ClearChildren(_contentCard);
                _displayedKey = null;
                _displayedSig = null;
            }
        }

        private void StartPage(IEnumerator co) => _pageCos.Add(StartCoroutine(co));

        private void StopPageAnims()
        {
            foreach (var c in _pageCos) if (c != null) StopCoroutine(c);
            _pageCos.Clear();
        }

        private void ShowPageInstant()
        {
            if (_contentCg != null) _contentCg.alpha = 1f;
            foreach (var a in _pageAnims) if (a.Cg != null) a.Cg.alpha = 1f;
            if (_pageValueText != null) _pageValueText.text = $"₽ {_pageValueTarget:N0}";
        }

        private IEnumerator AnimatePage()
        {
            foreach (var a in _pageAnims) if (a.Cg != null) a.Cg.alpha = 0f;
            if (_contentCg != null) _contentCg.alpha = 0f;
            if (_pageValueText != null) _pageValueText.text = "₽ 0";

            StartPage(FadeCanvas(_contentCg, 0.2f));
            StartPage(CountUpValue(_pageValueText, _pageValueTarget, _pageValueColor, 0.9f));

            yield return new WaitForSecondsRealtime(0.05f);
            foreach (var a in _pageAnims)
            {
                StartPage(FadeSlideIn(a, 0.28f));
                yield return new WaitForSecondsRealtime(0.05f);
            }
        }

        private static IEnumerator FadeCanvas(CanvasGroup cg, float dur)
        {
            if (cg == null) yield break;
            float t = 0f;
            while (t < dur) { t += Time.unscaledDeltaTime; cg.alpha = Mathf.Clamp01(t / dur); yield return null; }
            cg.alpha = 1f;
        }

        private static IEnumerator CountUpValue(TextMeshProUGUI label, double target, Color color, float dur)
        {
            if (label == null) yield break;
            label.color = color;
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                double v = target * Mathf.Clamp01(t / dur);
                label.text = $"₽ {v:N0}";
                yield return null;
            }
            label.text = $"₽ {target:N0}";
        }

        private static IEnumerator FadeSlideIn(PageAnim a, float dur)
        {
            if (a.Rt == null) yield break;
            Vector2 home  = a.Rt.anchoredPosition;
            Vector2 start = home + a.From;
            a.Rt.anchoredPosition = start;
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float p = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f);
                a.Rt.anchoredPosition = Vector2.Lerp(start, home, p);
                if (a.Cg != null) a.Cg.alpha = Mathf.Clamp01(t / dur * 2f);
                yield return null;
            }
            a.Rt.anchoredPosition = home;
            if (a.Cg != null) a.Cg.alpha = 1f;
        }

        private static string Signature(RaidStats s)
        {
            if (s == null) return "∅";
            var sb = new System.Text.StringBuilder();
            sb.Append(s.TotalFoundValue).Append('|').Append(s.ItemsFound).Append('|')
              .Append(s.PmcKills).Append('|').Append(s.ScavKills).Append('|')
              .Append(s.XpEarned).Append('|').Append(s.XpBonus).Append('|')
              .Append(s.PlayerSurvived).Append('|').Append(s.PlayerName);
            if (s.TopItems != null)
                foreach (var it in s.TopItems) sb.Append('|').Append(it.Name).Append('=').Append(it.Value);
            return sb.ToString();
        }

        private void BuildTabs(List<Entry> entries)
        {
            ClearChildren(_tabBar);

            double topVal = entries.Count > 1 ? entries.Max(e => e.Stats?.TotalFoundValue ?? 0) : -1;

            bool anyNew = false;
            foreach (var e in entries)
            {
                bool selected = e.Key == _selectedKey;
                bool alive    = e.Stats?.PlayerSurvived ?? false;
                bool isTop     = topVal > 0 && (e.Stats?.TotalFoundValue ?? 0) >= topVal;
                bool isNew     = !_firstTabBuild && !_knownTabKeys.Contains(e.Key);

                string nm = e.IsLocal ? "You" : e.Name;

                var tab = MakeRect("Tab", _tabBar);
                var le = tab.AddComponent<LayoutElement>();
                le.preferredWidth = 208f; le.minWidth = 150f; le.preferredHeight = 62f;
                var tabBg = tab.AddComponent<Image>();
                tabBg.color = selected
                    ? new Color(0.16f, 0.15f, 0.07f, 0.98f)
                    : new Color(0.085f, 0.095f, 0.115f, 0.95f);
                var tabCg = tab.AddComponent<CanvasGroup>();

                MakeBar("u", tab.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), selected ? 3f : 1f,
                        selected ? Gold : new Color(1f, 1f, 1f, 0.10f));

                if (isTop)
                {
                    var stripe = MakeRect("TopStripe", tab.transform);
                    var sRt = stripe.GetComponent<RectTransform>();
                    sRt.anchorMin = new Vector2(0f, 0f); sRt.anchorMax = new Vector2(0f, 1f);
                    sRt.pivot = new Vector2(0f, 0.5f);
                    sRt.anchoredPosition = Vector2.zero; sRt.sizeDelta = new Vector2(3f, 0f);
                    stripe.AddComponent<Image>().color = Gold;
                }

                var av = MakeRect("Avatar", tab.transform);
                var avRt = av.GetComponent<RectTransform>();
                avRt.anchorMin = avRt.anchorMax = new Vector2(0f, 0.5f); avRt.pivot = new Vector2(0f, 0.5f);
                avRt.anchoredPosition = new Vector2(13f, 0f); avRt.sizeDelta = new Vector2(36f, 36f);
                var avImg = av.AddComponent<Image>();
                avImg.sprite = CircleSprite(); avImg.type = Image.Type.Simple;
                avImg.color = AvatarColor(nm, e.IsLocal);
                var ini = MakeTMP("Ini", av.transform, 16f, FontStyles.Bold, TextAlignmentOptions.Center);
                ini.color = new Color(0.06f, 0.06f, 0.08f); ini.text = Initials(nm);
                Stretch(ini.rectTransform);

                var label = MakeTMP("L", tab.transform, 16f, selected ? FontStyles.Bold : FontStyles.Normal, TextAlignmentOptions.Left);
                label.color = selected ? Gold : new Color(0.82f, 0.82f, 0.86f);
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Ellipsis;
                label.text = nm;
                Place(label.rectTransform, 0f, 1f, 58f, -11f, 124f, 22f);

                var dot = MakeTMP("Dot", tab.transform, 13f, FontStyles.Bold, TextAlignmentOptions.Right);
                dot.color = alive ? SurvivedC : DiedC;
                dot.text  = "●";
                Place(dot.rectTransform, 1f, 1f, -12f, -12f, 14f, 16f);

                var val = MakeTMP("V", tab.transform, 14f, FontStyles.Bold, TextAlignmentOptions.Left);
                val.color = ValueColor(e.Stats?.TotalFoundValue ?? 0);
                val.text  = ShortValue(e.Stats?.TotalFoundValue ?? 0);
                Place(val.rectTransform, 0f, 1f, 58f, -35f, 140f, 18f);

                string key = e.Key;
                var btn = tab.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() =>
                {
                    if (_selectedKey == key) return;
                    _selectedKey = key;
                    PlaySound("ButtonClick", "MenuButtonClick");
                    Rebuild(animateContent: true);
                });

                if (isNew)
                {
                    anyNew = true;
                    StartCoroutine(AnimateNewTab(tab.GetComponent<RectTransform>(), tabCg, tabBg));
                }
            }

            if (anyNew) PlaySound("MenuDropdownSelect", "MenuCheckBox");

            _firstTabBuild = false;
            _knownTabKeys.Clear();
            foreach (var e in entries) _knownTabKeys.Add(e.Key);
        }

        private static IEnumerator AnimateNewTab(RectTransform rt, CanvasGroup cg, Image bg)
        {
            if (rt == null) yield break;
            Color baseColor = bg != null ? bg.color : Color.clear;
            Color flash      = new Color(0.28f, 0.25f, 0.09f, 0.98f);
            float dur = 0.34f;
            float t = 0f;
            while (t < dur)
            {
                if (rt == null) yield break;
                t += Time.unscaledDeltaTime;
                float p = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f);
                rt.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, p);
                if (cg != null) cg.alpha = Mathf.Clamp01(t / dur * 2f);
                if (bg != null) bg.color = Color.Lerp(flash, baseColor, p);
                yield return null;
            }
            if (rt != null) rt.localScale = Vector3.one;
            if (cg != null) cg.alpha = 1f;
            if (bg != null) bg.color = baseColor;
        }

        private void BuildPage(Entry e)
        {
            var s = e.Stats;
            bool alive = s.PlayerSurvived;
            _pageAnims.Clear();

            var name = MakeTMP("Name", _contentCard, 28f, FontStyles.Bold, TextAlignmentOptions.Left);
            name.color = e.IsLocal ? Gold : Color.white;
            name.text  = e.IsLocal ? "You" : e.Name;
            name.enableWordWrapping = false; name.overflowMode = TextOverflowModes.Ellipsis;
            Place(name.rectTransform, 0f, 1f, 40f, -20f, 460f, 38f);

            var status = MakeTMP("Status", _contentCard, 16f, FontStyles.Bold, TextAlignmentOptions.Right);
            status.color = alive ? SurvivedC : DiedC;
            status.text  = alive ? "● SURVIVED" : "● KIA";
            Place(status.rectTransform, 1f, 1f, -28f, -24f, 240f, 24f);

            MakeBar("Div", _contentCard.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), 1f, new Color(1f, 0.84f, 0f, 0.22f))
                .anchoredPosition = new Vector2(0f, -66f);

            var colDiv = MakeRect("ColDiv", _contentCard);
            var cdRt = colDiv.GetComponent<RectTransform>();
            cdRt.anchorMin = new Vector2(0f, 0f); cdRt.anchorMax = new Vector2(0f, 1f);
            cdRt.pivot = new Vector2(0.5f, 0.5f);
            cdRt.anchoredPosition = new Vector2(406f, -10f);
            cdRt.sizeDelta = new Vector2(1f, -150f);
            colDiv.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.06f);

            var value = MakeTMP("Value", _contentCard, 42f, FontStyles.Bold, TextAlignmentOptions.Left);
            value.color = ValueColor(s.TotalFoundValue);
            value.text  = $"₽ {s.TotalFoundValue:N0}";
            Place(value.rectTransform, 0f, 1f, 40f, -88f, 360f, 48f);
            _pageValueText   = value;
            _pageValueTarget = s.TotalFoundValue;
            _pageValueColor  = ValueColor(s.TotalFoundValue);

            var valLabel = MakeTMP("ValLbl", _contentCard, 12f, FontStyles.Normal, TextAlignmentOptions.Left);
            valLabel.color = new Color(0.5f, 0.5f, 0.55f); valLabel.characterSpacing = 2f;
            valLabel.text  = "TOTAL EXTRACTED";
            Place(valLabel.rectTransform, 0f, 1f, 42f, -138f, 360f, 16f);

            BuildStatRow(-182f, "ITEMS LOOTED", s.ItemsFound.ToString(), Gold,                                Color.white);
            BuildStatRow(-238f, "PMC KILLS",    s.PmcKills.ToString(),   new Color(1f, 0.27f, 0.27f),         s.PmcKills > 0 ? new Color(1f, 0.5f, 0.5f) : Color.white);
            BuildStatRow(-294f, "SCAV KILLS",   s.ScavKills.ToString(),  new Color(0.55f, 0.55f, 0.58f),      Color.white);

            if (s.XpEarned > 0)
            {
                var xpGo = MakeRect("XpLine", _contentCard);
                var xpRt = xpGo.GetComponent<RectTransform>();
                xpRt.anchorMin = xpRt.anchorMax = new Vector2(0f, 0f); xpRt.pivot = new Vector2(0f, 0f);
                xpRt.anchoredPosition = new Vector2(43f, 26f); xpRt.sizeDelta = new Vector2(360f, 24f);
                var xpCg = xpGo.AddComponent<CanvasGroup>();
                var xp = MakeTMP("Xp", xpGo.transform, 16f, FontStyles.Bold, TextAlignmentOptions.Left);
                xp.color = new Color(0.55f, 0.78f, 1f);
                xp.text  = s.XpBonus > 0 ? $"+{s.XpEarned:N0} XP   (+{s.XpBonus:N0} survival bonus)" : $"+{s.XpEarned:N0} XP";
                Stretch(xp.rectTransform);
                _pageAnims.Add(new PageAnim { Cg = xpCg, Rt = xpRt, From = new Vector2(0f, -12f) });
            }

            var hdr = MakeTMP("FindsHdr", _contentCard, 13f, FontStyles.Bold, TextAlignmentOptions.Left);
            hdr.color = Gold; hdr.characterSpacing = 3f;
            hdr.text  = "TOP FINDS";
            Place(hdr.rectTransform, 0f, 1f, 430f, -86f, 340f, 18f);

            int rows = Math.Min(s.TopItems?.Count ?? 0, 5);
            for (int i = 0; i < rows; i++)
            {
                var (itemName, itemVal) = s.TopItems[i];
                BuildFindRow(-120f - i * 44f, i + 1, itemName, itemVal);
            }
            if (rows == 0)
            {
                var none = MakeTMP("NoFinds", _contentCard, 14f, FontStyles.Italic, TextAlignmentOptions.Left);
                none.color = new Color(0.45f, 0.45f, 0.5f);
                none.text  = "No notable loot.";
                Place(none.rectTransform, 0f, 1f, 432f, -124f, 340f, 22f);
            }
        }

        private void BuildStatRow(float y, string label, string num, Color accent, Color numColor)
        {
            var go = MakeRect("StatRow", _contentCard);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(40f, y); rt.sizeDelta = new Vector2(345f, 48f);
            var cg = go.AddComponent<CanvasGroup>();

            var acc = MakeRect("Accent", go.transform);
            var aRt = acc.GetComponent<RectTransform>();
            aRt.anchorMin = Vector2.zero; aRt.anchorMax = new Vector2(0f, 1f); aRt.pivot = new Vector2(0f, 0.5f);
            aRt.anchoredPosition = Vector2.zero; aRt.sizeDelta = new Vector2(3f, 0f);
            acc.AddComponent<Image>().color = accent;

            var n = MakeTMP("Num", go.transform, 27f, FontStyles.Bold, TextAlignmentOptions.Left);
            n.color = numColor; n.text = num;
            Place(n.rectTransform, 0f, 0.5f, 14f, 0f, 80f, 34f);

            var l = MakeTMP("Lbl", go.transform, 13f, FontStyles.Normal, TextAlignmentOptions.Left);
            l.color = new Color(0.6f, 0.6f, 0.63f); l.text = label; l.characterSpacing = 1f;
            Place(l.rectTransform, 0f, 0.5f, 96f, 0f, 240f, 20f);

            _pageAnims.Add(new PageAnim { Cg = cg, Rt = rt, From = new Vector2(0f, -14f) });
        }

        private void BuildFindRow(float y, int rank, string itemName, double itemVal)
        {
            var go = MakeRect("Find", _contentCard);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(430f, y); rt.sizeDelta = new Vector2(352f, 40f);
            var cg = go.AddComponent<CanvasGroup>();

            go.AddComponent<Image>().color = new Color(0.09f, 0.10f, 0.13f, (rank % 2 == 1) ? 0.55f : 0.30f);

            var acc = MakeRect("Accent", go.transform);
            var aRt = acc.GetComponent<RectTransform>();
            aRt.anchorMin = Vector2.zero; aRt.anchorMax = new Vector2(0f, 1f); aRt.pivot = new Vector2(0f, 0.5f);
            aRt.anchoredPosition = Vector2.zero; aRt.sizeDelta = new Vector2(3f, 0f);
            acc.AddComponent<Image>().color = RarityColor(itemVal);

            var badge = MakeRect("Badge", go.transform);
            var bRt = badge.GetComponent<RectTransform>();
            bRt.anchorMin = bRt.anchorMax = new Vector2(0f, 0.5f); bRt.pivot = new Vector2(0f, 0.5f);
            bRt.anchoredPosition = new Vector2(12f, 0f); bRt.sizeDelta = new Vector2(32f, 20f);
            badge.AddComponent<Image>().color = RankColor(rank - 1);
            var bl = MakeTMP("BL", badge.transform, 11f, FontStyles.Bold, TextAlignmentOptions.Center);
            bl.color = new Color(0.05f, 0.05f, 0.05f); bl.text = RankLabel(rank - 1);
            Stretch(bl.rectTransform);

            var nm = MakeTMP("FindName", go.transform, 14f, FontStyles.Normal, TextAlignmentOptions.Left);
            nm.color = new Color(0.88f, 0.88f, 0.9f);
            nm.enableWordWrapping = false; nm.overflowMode = TextOverflowModes.Ellipsis;
            nm.text = itemName;
            var nr = nm.rectTransform;
            nr.anchorMin = new Vector2(0f, 0f); nr.anchorMax = new Vector2(1f, 1f); nr.pivot = new Vector2(0f, 0.5f);
            nr.offsetMin = new Vector2(52f, 0f); nr.offsetMax = new Vector2(-88f, 0f);

            var vl = MakeTMP("FindVal", go.transform, 14f, FontStyles.Bold, TextAlignmentOptions.Right);
            vl.color = ValueColor(itemVal); vl.text = ShortValue(itemVal);
            var vr = vl.rectTransform;
            vr.anchorMin = new Vector2(1f, 0f); vr.anchorMax = new Vector2(1f, 1f); vr.pivot = new Vector2(1f, 0.5f);
            vr.anchoredPosition = new Vector2(-10f, 0f); vr.sizeDelta = new Vector2(82f, 0f);

            _pageAnims.Add(new PageAnim { Cg = cg, Rt = rt, From = new Vector2(16f, 0f) });
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
            _titleText.text  = "TEAM SUMMARY";
            Place(_titleText.rectTransform, 0.5f, 1f, 0f, -50f, 1200f, 52f);

            _subtitleText = MakeTMP("Subtitle", _root.transform, 20f, FontStyles.Normal, TextAlignmentOptions.Center);
            _subtitleText.color = new Color(0.7f, 0.7f, 0.75f);
            Place(_subtitleText.rectTransform, 0.5f, 1f, 0f, -100f, 1200f, 28f);

            var panelGo = MakeRect("Panel", _root.transform);
            _panel = panelGo.GetComponent<RectTransform>();
            Stretch(_panel);

            var tabsGo = MakeRect("Tabs", panelGo.transform);
            _tabBar = tabsGo.GetComponent<RectTransform>();
            _tabBar.anchorMin = _tabBar.anchorMax = new Vector2(0.5f, 1f);
            _tabBar.pivot = new Vector2(0.5f, 1f);
            _tabBar.anchoredPosition = new Vector2(0f, -150f);
            _tabBar.sizeDelta = new Vector2(100f, 40f);
            var hlg = tabsGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f; hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            var fit = tabsGo.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            var cardGo = MakeRect("ContentCard", panelGo.transform);
            _contentCard = cardGo.GetComponent<RectTransform>();
            _contentCard.anchorMin = _contentCard.anchorMax = new Vector2(0.5f, 0.5f);
            _contentCard.pivot = new Vector2(0.5f, 0.5f);
            _contentCard.anchoredPosition = new Vector2(0f, -40f);
            _contentCard.sizeDelta = new Vector2(CardW, CardH);
            cardGo.AddComponent<Image>().color = new Color(0.05f, 0.06f, 0.08f, 0.96f);
            _contentCg = cardGo.AddComponent<CanvasGroup>();

            var cardStripe = MakeRect("CardStripe", panelGo.transform);
            var csRt = cardStripe.GetComponent<RectTransform>();
            csRt.anchorMin = csRt.anchorMax = new Vector2(0.5f, 0.5f);
            csRt.pivot = new Vector2(0.5f, 0.5f);
            csRt.anchoredPosition = new Vector2(-CardW / 2f, -40f);
            csRt.sizeDelta = new Vector2(3f, CardH);
            cardStripe.AddComponent<Image>().color = Gold;

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
            hint.text  = "Click anywhere to close";
            Place(hint.rectTransform, 0.5f, 0f, 0f, 24f, 600f, 24f);
        }

        private static void ClearChildren(RectTransform rt)
        {
            for (int i = rt.childCount - 1; i >= 0; i--) Destroy(rt.GetChild(i).gameObject);
        }

        private static string ShortValue(double v)
        {
            if (v >= 1_000_000) return $"₽{v / 1_000_000:0.0}M";
            if (v >= 1_000)     return $"₽{v / 1_000:0}k";
            return $"₽{v:0}";
        }

        private static string ColorHex(Color c) => ColorUtility.ToHtmlStringRGB(c);

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
