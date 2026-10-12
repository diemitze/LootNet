using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using LootNet.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace LootNet.UI
{
    /// Stash-grid layout: every find is laid out as an item cell at its real grid size,
    /// dropping in from most to least valuable while the total ticks up beside it.
    public class RaidSummaryDisplay : MonoBehaviour
    {
        private static RaidSummaryDisplay _instance;
        public static RaidSummaryDisplay Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("LootNetSummary");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<RaidSummaryDisplay>();
                }
                return _instance;
            }
        }

        public event Action OnHidden;

        private const float AutoDismissSeconds = 15f;
        private const float FadeInDuration     = 0.5f;
        private const double BigItemValue      = 50_000;

        // 1920x1080 frame, origin top-left. The frame is centred so wide screens just get margin.
        private const float FrameW    = 1920f;
        private const float FrameH    = 1080f;
        private const float GridLeft  = 96f;
        private const float GridTop   = 165f;
        private const float GridWidth = 880f;
        private const float GridMaxH  = 700f;
        private const float HeaderH   = 44f;
        private const float MoreH     = 44f;
        private const float SideLeft  = 1095f;
        private const float SideWidth = 730f;
        private const float TitleY    = 36f;

        // Biggest first; the first size that fits every item wins.
        private static readonly float[] CellSizes = { 72f, 63f, 54f, 46f, 40f };

        private static readonly Color Gold   = new(0.89f, 0.76f, 0.35f);
        private static readonly Color Ok     = new(0.53f, 0.77f, 0.39f);
        private static readonly Color Bad    = new(0.89f, 0.34f, 0.29f);
        private static readonly Color Muted  = new(0.55f, 0.58f, 0.54f);
        private static readonly Color Bright = new(0.95f, 0.95f, 0.93f);
        private static readonly Color Line   = new(0.23f, 0.25f, 0.24f);

        private CanvasGroup     _canvasGroup;
        private GameObject      _root;
        private RectTransform   _frame;
        private RawImage        _bgImage;
        private RawImage        _redCast;

        private RectTransform   _window;
        private CanvasGroup     _windowCg;
        private RectTransform   _gridRt;
        private RawImage        _gridLines;
        private TextMeshProUGUI _headerCount;
        private TextMeshProUGUI _emptyText;
        private RectTransform   _moreRow;
        private TextMeshProUGUI _moreLeft;
        private TextMeshProUGUI _moreRight;
        private readonly List<Cell> _cells = new();
        private double          _hiddenValue;
        private Color           _iconTint = Color.white;

        private TextMeshProUGUI _kickerText;
        private TextMeshProUGUI _titleText;
        private TextMeshProUGUI _labelText;
        private TextMeshProUGUI _totalText;
        private RectTransform   _strike;
        private RectTransform   _chip;
        private CanvasGroup     _chipCg;
        private TextMeshProUGUI _chipText;
        private CanvasGroup     _statsCg;
        private TextMeshProUGUI _pmcText;
        private TextMeshProUGUI _scavText;
        private TextMeshProUGUI _xpText;
        private TextMeshProUGUI _bonusText;
        private TextMeshProUGUI _quipText;
        private CanvasGroup     _quipCg;
        private CanvasGroup     _squadCg;
        private TextMeshProUGUI _squadHeader;
        private readonly List<MateRow> _mateRows = new();
        private const int       MaxMateRows = 5;
        private const float     MateRowH    = 28f;
        private CanvasGroup     _footCg;
        private TextMeshProUGUI _metaText;

        private RectTransform   _progressBarFill;
        private Texture2D       _vignetteTexture;
        private Texture2D       _dimTexture;
        private Texture2D       _scrimTexture;
        private Texture2D       _gridTexture;
        private RenderTexture   _bgBlurTexture;

        private GameObject      _videoPanel;
        private CanvasGroup     _videoCg;
        private RawImage        _videoImage;
        private VideoPlayer     _videoPlayer;
        private RenderTexture   _videoRenderTexture;
        private const float     VideoMaxWidth = 320f;
        private const string    VideoFileName = "weii weii.mp4";

        private float _timer;
        private bool  _visible;

        private class Cell
        {
            public LootEntry       Entry;
            public RectTransform   Rt;
            public CanvasGroup     Cg;
            public Image           Bg;
            public Image           Icon;
        }

        private struct MateRow
        {
            public CanvasGroup     Cg;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Detail;
        }

        private struct Placement
        {
            public LootEntry Entry;
            public int X, Y;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            BuildUI();
        }

        private void Update()
        {
            if (!_visible) return;
            if (Plugin.ManualDismiss.Value) return;

            _timer -= Time.deltaTime;
            if (_progressBarFill != null)
                _progressBarFill.anchorMax = new Vector2(Mathf.Clamp01(_timer / AutoDismissSeconds), 1f);
            if (_timer <= 0f) Hide();
        }

        public void QueueSummary(RaidStats stats)
        {
            if (_visible) return;
            StopAllCoroutines();
            StartCoroutine(CaptureAndShow(stats));
        }

        /// Re-shows a past raid from the menu. Screenshot is whatever is on screen now.
        public void Replay(RaidStats stats)
        {
            if (stats == null || _visible) return;
            OnHidden = null;
            QueueSummary(stats);
        }

        private IEnumerator CaptureAndShow(RaidStats stats)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            if (_bgImage != null) _bgImage.texture = Defocus(tex);
            Destroy(tex);
            yield return StartCoroutine(AnimateIn(stats));
        }

        public IEnumerator PrepareVideoEarly()
        {
            string path = Path.Combine(BepInEx.Paths.PluginPath, "LootNet", VideoFileName);
            if (!File.Exists(path)) yield break;
            if (_videoPlayer.isPrepared || _videoPlayer.isPlaying) yield break;
            _videoPlayer.url = "file:///" + path.Replace("\\", "/");
            _videoPlayer.Prepare();
        }

        public void Hide()
        {
            StopAllCoroutines();
            if (_visible) LootSounds.Play(LootSounds.Close, "MenuEscape");
            _visible = false;
            StartCoroutine(FadeOutAndReveal());
        }

        private IEnumerator FadeOutAndReveal()
        {
            const float fadeDur = 0.6f;
            float t = 0f;
            float startAlpha = _canvasGroup != null ? _canvasGroup.alpha : 0f;
            while (t < fadeDur)
            {
                t += Time.deltaTime;
                if (_canvasGroup != null)
                    _canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, t / fadeDur);
                yield return null;
            }
            if (_root != null) _root.SetActive(false);
            if (_canvasGroup != null) _canvasGroup.alpha = 0f;
            ClearCells();
            OnHidden?.Invoke();
            OnHidden = null;
        }

        private IEnumerator AnimateIn(RaidStats stats)
        {
            ResetUI(stats);
            _canvasGroup.alpha = 0f;
            _root.SetActive(true);
            _visible = true;
            _timer   = AutoDismissSeconds;

            bool died = !stats.PlayerSurvived;
            LootSounds.Play(died ? LootSounds.EnterDied : LootSounds.EnterSurvived, "MenuOpenContainer");

            yield return StartCoroutine(FadeCanvasGroup(_canvasGroup, FadeInDuration));

            LootSounds.Play(LootSounds.Bars, "MenuOpenContainer");
            StartCoroutine(Rise(_window, _windowCg, 14f, 0.45f));
            StartCoroutine(FillIcons());

            yield return new WaitForSeconds(0.15f);
            StartCoroutine(FadeText(_kickerText, died ? Bad : Ok, 0.5f));
            yield return new WaitForSeconds(0.15f);
            StartCoroutine(RiseText(_titleText, Bright, 10f, 0.6f));
            yield return new WaitForSeconds(0.15f);
            StartCoroutine(FadeText(_labelText, Muted, 0.5f));
            StartCoroutine(FadeText(_totalText, Gold, 0.5f));
            yield return new WaitForSeconds(0.2f);

            // Valuable items land one by one with a tick. The filler pours in, a few per frame
            // when there is a lot of it, so a 150-item raid still settles in a couple of seconds.
            int small = 0;
            foreach (var c in _cells) if (c.Entry.Value < BigItemValue) small++;
            int perFrame = Mathf.Max(1, Mathf.CeilToInt(small / 45f));

            double running = 0;
            int pending = 0;
            foreach (var c in _cells)
            {
                StartCoroutine(PopCell(c));
                running += c.Entry.Value;
                _totalText.text = Rub(running);

                if (c.Entry.Value >= BigItemValue)
                {
                    LootSounds.Play(LootSounds.Tick, "MenuCheckBox");
                    yield return new WaitForSeconds(0.13f);
                }
                else if (++pending >= perFrame)
                {
                    pending = 0;
                    yield return new WaitForSeconds(0.03f);
                }
            }

            if (_hiddenValue > 0)
                yield return StartCoroutine(CountUp(_totalText, running, running + _hiddenValue, 0.5f));
            _totalText.text = Rub(stats.TotalFoundValue);
            LootSounds.Play(LootSounds.Total, "MenuCheckBox");

            if (_moreRow.gameObject.activeSelf)
                StartCoroutine(FadeCanvasGroup(_moreRow.GetComponent<CanvasGroup>(), 0.4f));

            if (!string.IsNullOrEmpty(_chipText.text))
                StartCoroutine(FadeCanvasGroup(_chipCg, 0.5f));

            yield return new WaitForSeconds(0.2f);
            StartCoroutine(FadeCanvasGroup(_statsCg, 0.5f));
            StartCoroutine(WatchForXpBonus(stats));

            if (died)
            {
                yield return new WaitForSeconds(0.3f);
                StartCoroutine(LoseGrid(1.0f));
                StartCoroutine(StrikeTotal(0.5f));
            }

            StartCoroutine(PlayVideoSequence());
            yield return new WaitForSeconds(died ? 0.9f : 0.5f);
            yield return StartCoroutine(RevealSquad());

            if (!string.IsNullOrEmpty(_quipText.text))
            {
                LootSounds.Play(died ? LootSounds.ResolveDied : LootSounds.ResolveSurvived, "MenuCheckBox");
                yield return StartCoroutine(FadeCanvasGroup(_quipCg, 0.8f));
            }

            yield return new WaitForSeconds(0.2f);
            yield return StartCoroutine(FadeCanvasGroup(_footCg, 0.6f));
        }

        // ---------- data ----------

        /// History saved before the full loot list existed only has the top five, so those
        /// become 1x1 cells and the rest is folded into the "more items" row.
        private static List<LootEntry> EntriesFor(RaidStats stats)
        {
            if (stats.Loot != null) return stats.Loot;

            var list = new List<LootEntry>();
            if (stats.TopItems == null) return list;
            for (int i = 0; i < stats.TopItems.Count; i++)
            {
                var (name, value) = stats.TopItems[i];
                string tpl = stats.TopItemTemplates != null && i < stats.TopItemTemplates.Count ? stats.TopItemTemplates[i] : null;
                list.Add(new LootEntry { Name = name, ShortName = name, TemplateId = tpl, Value = value });
            }
            return list;
        }

        private static float Layout(List<LootEntry> items, out List<Placement> placed, out int cols, out int rows)
        {
            placed = null; cols = 0; rows = 0;
            foreach (float cell in CellSizes)
            {
                cols = Mathf.FloorToInt(GridWidth / cell);
                int maxRows = Mathf.FloorToInt(GridMaxH / cell);
                int skipped = Pack(items, cols, maxRows, out placed, out rows);
                if (skipped == 0 || cell == CellSizes[CellSizes.Length - 1]) return cell;
            }
            return CellSizes[CellSizes.Length - 1];
        }

        /// First-fit, top to bottom, left to right, in value order. Anything that no longer
        /// fits is left out and counted in the "more items" row.
        private static int Pack(List<LootEntry> items, int cols, int maxRows, out List<Placement> placed, out int rows)
        {
            var occ = new bool[maxRows, cols];
            placed = new List<Placement>(items.Count);
            rows = 0;
            int skipped = 0;

            foreach (var e in items)
            {
                int w = Mathf.Clamp(e.Width, 1, cols), h = Mathf.Clamp(e.Height, 1, maxRows);
                bool done = false;
                for (int y = 0; y + h <= maxRows && !done; y++)
                for (int x = 0; x + w <= cols && !done; x++)
                {
                    if (!Free(occ, x, y, w, h)) continue;
                    for (int yy = y; yy < y + h; yy++)
                    for (int xx = x; xx < x + w; xx++)
                        occ[yy, xx] = true;
                    placed.Add(new Placement { Entry = e, X = x, Y = y });
                    rows = Mathf.Max(rows, y + h);
                    done = true;
                }
                if (!done) skipped++;
            }
            return skipped;
        }

        private static bool Free(bool[,] occ, int x, int y, int w, int h)
        {
            for (int yy = y; yy < y + h; yy++)
            for (int xx = x; xx < x + w; xx++)
                if (occ[yy, xx]) return false;
            return true;
        }

        /// Where this raid lands against the ones before it. Null when there is nothing to
        /// compare against yet.
        private static string BuildComparison(RaidStats stats)
        {
            double value = stats.TotalFoundValue;
            if (value <= 0) return null;

            var prior = new List<double>();
            foreach (var h in RaidTracker.RaidHistory)
            {
                if (ReferenceEquals(h, stats)) continue;   // the current raid is already in history
                if (h.TotalFoundValue > 0) prior.Add(h.TotalFoundValue);
            }
            if (prior.Count == 0) return null;

            int rank = 1;
            foreach (var v in prior) if (v > value) rank++;

            int total = prior.Count + 1;
            bool died = !stats.PlayerSurvived;
            if (rank == 1) return died ? "WOULD HAVE BEEN YOUR BEST RAID" : "BEST RAID YET";
            if (rank <= 3) return died ? $"WOULD HAVE BEEN YOUR {Ordinal(rank)} BEST" : $"{Ordinal(rank)} BEST OF YOUR LAST {total}";

            double sum = 0;
            foreach (var v in prior) sum += v;
            double avg = sum / prior.Count;
            if (avg <= 0) return null;

            int pct = Mathf.RoundToInt((float)((value - avg) / avg * 100.0));
            if (pct == 0) return "DEAD ON YOUR AVERAGE";
            return pct > 0 ? $"+{pct}% VS YOUR AVERAGE" : $"{pct}% VS YOUR AVERAGE";
        }

        private static string Ordinal(int n) => n == 2 ? "2ND" : n == 3 ? "3RD" : $"{n}TH";

        // ---------- state ----------

        private void ResetUI(RaidStats stats)
        {
            bool died = !stats.PlayerSurvived;

            _redCast.color = new Color(0.55f, 0.08f, 0.05f, died ? 0.35f : 0f);

            _kickerText.text  = died ? "KILLED IN ACTION" : "EXTRACTED";
            _kickerText.color = Color.clear;
            _titleText.text   = died ? "You did not make it out." : "You made it out.";
            _titleText.color  = Color.clear;
            // A Hide() mid-animation leaves these offset, so pin them back every time.
            _titleText.rectTransform.anchoredPosition = new Vector2(SideLeft, -(GridTop + TitleY));
            _window.anchoredPosition = new Vector2(GridLeft, -GridTop);
            _labelText.text   = died ? "LOOT LOST" : "LOOT VALUE";
            _labelText.color  = Color.clear;
            _totalText.text   = Rub(0);
            _totalText.color  = Color.clear;
            _strike.sizeDelta = new Vector2(0f, _strike.sizeDelta.y);

            string cmp = BuildComparison(stats) ?? string.Empty;
            _chipText.text  = cmp;
            _chipText.color = died ? new Color(1f, 0.62f, 0.56f) : Gold;
            SetFrameColor(_chip, _chipText.color);
            _chip.sizeDelta = new Vector2(_chipText.GetPreferredValues(cmp).x + 28f, _chip.sizeDelta.y);
            _chipCg.alpha   = 0f;

            _pmcText.text  = stats.PmcKills.ToString();
            _scavText.text = stats.ScavKills.ToString();
            _xpText.text   = $"{stats.XpEarned:N0}";
            _bonusText.text  = string.Empty;
            _bonusText.color = new Color(Ok.r, Ok.g, Ok.b, 0f);
            _statsCg.alpha = 0f;

            _quipText.text = Quips.Pick(stats);
            _quipCg.alpha  = 0f;

            ResetSquad(stats);

            string map  = string.IsNullOrEmpty(stats.MapName) ? "UNKNOWN" : stats.MapName.ToUpperInvariant();
            string time = stats.RaidTime == default ? DateTime.Now.ToString("HH:mm") : stats.RaidTime.ToString("HH:mm");
            _metaText.text = $"{map}  ·  {time}  ·  " + (Plugin.ManualDismiss.Value ? "CLICK TO CONTINUE" : "CLICK ANYWHERE TO CLOSE");
            _footCg.alpha  = 0f;

            BuildGrid(stats);

            if (_progressBarFill != null)
            {
                _progressBarFill.anchorMax = new Vector2(1f, 1f);
                _progressBarFill.transform.parent.gameObject.SetActive(!Plugin.ManualDismiss.Value);
            }

            if (_videoPlayer != null) { _videoPlayer.Pause(); _videoPlayer.time = 0; }
            if (_videoCg     != null) _videoCg.alpha = 0f;
        }

        private void BuildGrid(RaidStats stats)
        {
            ClearCells();
            _iconTint = Color.white;
            _gridLines.color = Color.white;

            var entries = EntriesFor(stats);
            float cell = Layout(entries, out var placed, out int cols, out int rows);

            bool empty = placed.Count == 0;
            if (empty) { cell = CellSizes[0]; cols = Mathf.FloorToInt(GridWidth / cell); rows = 2; }
            _emptyText.gameObject.SetActive(empty);

            float gridW = cols * cell, gridH = rows * cell;
            _gridRt.sizeDelta = new Vector2(gridW, gridH);
            _gridLines.uvRect = new Rect(0f, 0f, cols, rows);

            double shownValue = 0;
            foreach (var p in placed)
            {
                _cells.Add(MakeCell(p, cell));
                shownValue += p.Entry.Value;
                LootIconCache.Request(p.Entry);
            }

            int hiddenCount = Mathf.Max(0, stats.ItemsFound - placed.Count);
            _hiddenValue = Math.Max(0, stats.TotalFoundValue - shownValue);
            bool more = hiddenCount > 0 && _hiddenValue > 0;
            _moreRow.gameObject.SetActive(more);
            _moreRow.GetComponent<CanvasGroup>().alpha = 0f;
            _moreLeft.text  = $"+{hiddenCount} more item{(hiddenCount == 1 ? "" : "s")}";
            _moreRight.text = Rub(_hiddenValue);

            _headerCount.text = $"{stats.ItemsFound} ITEM{(stats.ItemsFound == 1 ? "" : "S")}";
            _window.sizeDelta = new Vector2(gridW, HeaderH + gridH + (more ? MoreH : 0f));
            _windowCg.alpha   = 0f;
        }

        private Cell MakeCell(Placement p, float cell)
        {
            var e = p.Entry;
            int w = Mathf.Max(1, e.Width), h = Mathf.Max(1, e.Height);
            float pw = w * cell, ph = h * cell;

            var go = MakeRect("Cell", _gridRt);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);   // centre, so the pop scales in place
            rt.sizeDelta = new Vector2(pw - 2f, ph - 2f);
            rt.anchoredPosition = new Vector2(p.X * cell + pw / 2f, -(p.Y * cell + ph / 2f));

            var cg = go.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            var bg = AddImage(go, CellTint(e.Value));

            var iconGo = MakeRect("Icon", go.transform);
            var irt = iconGo.GetComponent<RectTransform>();
            irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
            irt.offsetMin = new Vector2(3f, 3f); irt.offsetMax = new Vector2(-3f, -3f);
            var icon = AddImage(iconGo, Color.white);
            icon.preserveAspect = true;
            icon.enabled = false;

            float fs = cell >= 54f ? 13f : 11f;
            var sn = MakeTMP("Short", go.transform, fs, FontStyles.Normal, TextAlignmentOptions.TopRight);
            sn.text = e.ShortName ?? e.Name ?? "";
            sn.color = new Color(0.86f, 0.88f, 0.84f);
            sn.enableWordWrapping = false;
            sn.overflowMode = TextOverflowModes.Ellipsis;
            Inset(sn.rectTransform, 4f, 2f);

            var val = MakeTMP("Value", go.transform, fs, FontStyles.Normal, TextAlignmentOptions.BottomRight);
            val.text = ShortRub(e.Value);
            val.color = ValueTint(e.Value);
            val.enableWordWrapping = false;
            Inset(val.rectTransform, 4f, 2f);

            if (e.Count > 1)
            {
                var cnt = MakeTMP("Count", go.transform, fs, FontStyles.Normal, TextAlignmentOptions.BottomLeft);
                cnt.text = $"x{e.Count}";
                cnt.color = new Color(0.86f, 0.88f, 0.84f);
                cnt.enableWordWrapping = false;
                Inset(cnt.rectTransform, 4f, 2f);
            }

            return new Cell { Entry = e, Rt = rt, Cg = cg, Bg = bg, Icon = icon };
        }

        private void ClearCells()
        {
            foreach (var c in _cells)
                if (c.Rt != null) Destroy(c.Rt.gameObject);
            _cells.Clear();
        }

        /// Icons are rendered by the game one after another, so a big raid can take several
        /// seconds. Cells pick theirs up whenever they land, for as long as the screen is up.
        /// A cell without one still reads fine as name and value.
        private IEnumerator FillIcons()
        {
            while (_visible)
            {
                bool pending = false;
                foreach (var c in _cells)
                {
                    if (c.Icon == null || c.Icon.enabled) continue;
                    string key = LootIconCache.KeyFor(c.Entry);
                    var s = LootIconCache.Get(key);
                    if (s != null)
                    {
                        c.Icon.sprite  = s;
                        c.Icon.color   = _iconTint;
                        c.Icon.enabled = true;
                    }
                    else
                    {
                        LootIconCache.Request(c.Entry);   // re-asks only once a render looks lost
                        if (LootIconCache.IsPending(key)) pending = true;
                    }
                }
                if (!pending) yield break;
                yield return new WaitForSecondsRealtime(0.1f);
            }
        }

        // ---------- animation ----------

        private static IEnumerator PopCell(Cell c)
        {
            const float dur = 0.24f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / dur);
                c.Cg.alpha = Mathf.Clamp01(p * 2f);
                c.Rt.localScale = Vector3.one * Mathf.LerpUnclamped(0.55f, 1f, EaseOutBack(p));
                yield return null;
            }
            c.Cg.alpha = 1f;
            c.Rt.localScale = Vector3.one;
        }

        private static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }

        /// The haul stayed on the body, so the grid drains to grey.
        private IEnumerator LoseGrid(float dur)
        {
            var dead     = new Color(0.20f, 0.21f, 0.20f, 0.7f);
            var deadIcon = new Color(0.38f, 0.38f, 0.38f, 0.85f);
            var startBg  = new List<Color>(_cells.Count);
            foreach (var c in _cells) startBg.Add(c.Bg.color);

            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float p = Mathf.SmoothStep(0f, 1f, t / dur);
                _iconTint = Color.Lerp(Color.white, deadIcon, p);
                for (int i = 0; i < _cells.Count; i++)
                {
                    _cells[i].Bg.color   = Color.Lerp(startBg[i], dead, p);
                    _cells[i].Icon.color = _iconTint;
                }
                _gridLines.color = new Color(1f, 1f, 1f, 1f - 0.6f * p);
                yield return null;
            }
            _iconTint = deadIcon;
        }

        private IEnumerator StrikeTotal(float dur)
        {
            float w = _totalText.GetPreferredValues(_totalText.text).x + 14f;
            var faded = new Color(0.59f, 0.53f, 0.35f);
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float p = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f);
                _strike.sizeDelta = new Vector2(w * p, _strike.sizeDelta.y);
                _totalText.color  = Color.Lerp(Gold, faded, p);
                yield return null;
            }
            _strike.sizeDelta = new Vector2(w, _strike.sizeDelta.y);
        }

        private IEnumerator WatchForXpBonus(RaidStats stats)
        {
            int baseXp  = stats.XpEarned;
            int bonusXp = stats.XpBonus;

            yield return new WaitForSecondsRealtime(1.5f);

            if (bonusXp <= 0)
            {
                float waitT = 0f;
                while (waitT < 3f)
                {
                    waitT += 0.2f;
                    yield return new WaitForSecondsRealtime(0.2f);
                    int b = RaidTracker.TryReadCounterTag("ExpExitStatus");
                    if (b > 0) { bonusXp = b; break; }
                }
            }

            if (bonusXp <= 0) yield break;

            string label = stats.PlayerSurvived ? "SURVIVED" : "EXTRACT";
            _bonusText.text = $"+{bonusXp:N0} {label} BONUS";

            int finalXp = baseXp + bonusXp;
            float dur = 0.8f, t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float eased = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f);
                _xpText.text = $"{Mathf.RoundToInt(Mathf.Lerp(baseXp, finalXp, eased)):N0}";
                _bonusText.color = new Color(Ok.r, Ok.g, Ok.b, eased);
                yield return null;
            }
            _xpText.text = $"{finalXp:N0}";
            _bonusText.color = Ok;
        }

        private static IEnumerator Rise(RectTransform rt, CanvasGroup cg, float rise, float dur)
        {
            var endPos   = rt.anchoredPosition;
            var startPos = endPos - new Vector2(0f, rise);
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float p = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f);
                rt.anchoredPosition = Vector2.Lerp(startPos, endPos, p);
                cg.alpha = Mathf.Clamp01(t / dur * 1.5f);
                yield return null;
            }
            rt.anchoredPosition = endPos;
            cg.alpha = 1f;
        }

        private static IEnumerator CountUp(TextMeshProUGUI label, double from, double to, float dur)
        {
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float p = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f);
                label.text = Rub(from + (to - from) * p);
                yield return null;
            }
            label.text = Rub(to);
        }

        private static IEnumerator RiseText(TextMeshProUGUI label, Color target, float rise, float dur)
        {
            var rt = label.rectTransform;
            var endPos   = rt.anchoredPosition;
            var startPos = endPos - new Vector2(0f, rise);
            rt.anchoredPosition = startPos;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float p = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f);
                rt.anchoredPosition = Vector2.Lerp(startPos, endPos, p);
                label.color = new Color(target.r, target.g, target.b, Mathf.Clamp01(t / dur * 1.4f));
                yield return null;
            }
            rt.anchoredPosition = endPos;
            label.color = target;
        }

        private static IEnumerator FadeText(TextMeshProUGUI label, Color target, float dur)
        {
            Color start = new Color(target.r, target.g, target.b, 0f);
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                label.color = Color.Lerp(start, target, t / dur);
                yield return null;
            }
            label.color = target;
        }

        private static IEnumerator FadeCanvasGroup(CanvasGroup cg, float dur)
        {
            float t = 0f;
            while (t < dur) { t += Time.deltaTime; cg.alpha = Mathf.Clamp01(t / dur); yield return null; }
            cg.alpha = 1f;
        }

        // ---------- construction ----------

        private void BuildUI()
        {
            var canvas = gameObject.GetComponent<Canvas>() ?? gameObject.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;

            // Expand keeps the whole 1920x1080 frame on screen at any aspect ratio.
            var scaler = gameObject.GetComponent<CanvasScaler>() ?? gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(FrameW, FrameH);
            scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.Expand;

            if (!gameObject.GetComponent<GraphicRaycaster>()) gameObject.AddComponent<GraphicRaycaster>();
            _canvasGroup = gameObject.GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

            _root = MakeRect("SummaryRoot", transform);
            Stretch(_root.GetComponent<RectTransform>());

            var screenshotGo = MakeRect("Screenshot", _root.transform);
            Stretch(screenshotGo.GetComponent<RectTransform>());
            _bgImage = screenshotGo.AddComponent<RawImage>();
            _bgImage.color = new Color(0.80f, 0.84f, 0.95f, 1f);

            _dimTexture = BuildDimTexture();
            var dimGo = MakeRect("Dim", _root.transform);
            Stretch(dimGo.GetComponent<RectTransform>());
            var dim = dimGo.AddComponent<RawImage>();
            dim.texture = _dimTexture;
            dim.color   = new Color(0.024f, 0.031f, 0.027f, 1f);

            _scrimTexture = BuildScrimTexture();
            var redGo = MakeRect("RedCast", _root.transform);
            Stretch(redGo.GetComponent<RectTransform>());
            _redCast = redGo.AddComponent<RawImage>();
            _redCast.texture = _scrimTexture;
            _redCast.color   = Color.clear;

            _vignetteTexture = BuildVignetteTexture();
            var vigGo = MakeRect("Vignette", _root.transform);
            Stretch(vigGo.GetComponent<RectTransform>());
            var vigImg = vigGo.AddComponent<RawImage>();
            vigImg.texture = _vignetteTexture;
            vigImg.color   = new Color(1f, 1f, 1f, 0.38f);

            var frameGo = MakeRect("Frame", _root.transform);
            _frame = frameGo.GetComponent<RectTransform>();
            _frame.anchorMin = _frame.anchorMax = new Vector2(0.5f, 0.5f);
            _frame.pivot = new Vector2(0.5f, 0.5f);
            _frame.sizeDelta = new Vector2(FrameW, FrameH);
            _frame.anchoredPosition = Vector2.zero;

            BuildWindow();
            BuildSide();
            BuildFoot();

            var progTrack = MakeRect("ProgressTrack", _root.transform);
            var ptRt = progTrack.GetComponent<RectTransform>();
            ptRt.anchorMin = new Vector2(0f, 0f); ptRt.anchorMax = new Vector2(1f, 0f);
            ptRt.pivot = new Vector2(0.5f, 0f);
            ptRt.anchoredPosition = Vector2.zero; ptRt.sizeDelta = new Vector2(0f, 2f);
            AddImage(progTrack, new Color(1f, 1f, 1f, 0.06f));

            var progFillGo = MakeRect("ProgressFill", progTrack.transform);
            _progressBarFill = progFillGo.GetComponent<RectTransform>();
            _progressBarFill.anchorMin = Vector2.zero; _progressBarFill.anchorMax = Vector2.one;
            _progressBarFill.sizeDelta = Vector2.zero; _progressBarFill.anchoredPosition = Vector2.zero;
            _progressBarFill.pivot = new Vector2(0f, 0.5f);
            AddImage(progFillGo, new Color(Gold.r, Gold.g, Gold.b, 0.5f));

            BuildVideoPanel();

            // Nothing else may eat clicks: the catcher goes on top and everything
            // underneath stops being a raycast target.
            foreach (var g in _root.GetComponentsInChildren<Graphic>(true))
                g.raycastTarget = false;

            var clickGo = MakeRect("ClickCatcher", _root.transform);
            Stretch(clickGo.GetComponent<RectTransform>());
            var clickImg = clickGo.AddComponent<Image>();
            clickImg.color = Color.clear;
            clickImg.raycastTarget = true;
            var btn = clickGo.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(Hide);
            clickGo.transform.SetAsLastSibling();

            _canvasGroup.alpha = 0f;
            _root.SetActive(false);
        }

        private void BuildWindow()
        {
            var go = MakeRect("LootWindow", _frame);
            _window = go.GetComponent<RectTransform>();
            TopLeft(_window, GridLeft, GridTop, GridWidth, 300f);
            _windowCg = go.AddComponent<CanvasGroup>();
            AddImage(go, new Color(0.051f, 0.059f, 0.055f, 0.94f));
            AddFrame(_window, new Color(0.23f, 0.25f, 0.24f));

            var head = MakeRect("Header", _window);
            var hrt = head.GetComponent<RectTransform>();
            hrt.anchorMin = new Vector2(0f, 1f); hrt.anchorMax = new Vector2(1f, 1f);
            hrt.pivot = new Vector2(0.5f, 1f);
            hrt.anchoredPosition = Vector2.zero; hrt.sizeDelta = new Vector2(0f, HeaderH);
            AddImage(head, new Color(0.125f, 0.141f, 0.129f, 1f));

            var title = MakeTMP("Title", head.transform, 15f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            title.text = "RAID LOOT";
            title.color = new Color(0.73f, 0.75f, 0.71f);
            title.characterSpacing = 6f;
            Inset(title.rectTransform, 16f, 0f);

            _headerCount = MakeTMP("Count", head.transform, 15f, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
            _headerCount.color = new Color(0.73f, 0.75f, 0.71f);
            _headerCount.characterSpacing = 6f;
            Inset(_headerCount.rectTransform, 16f, 0f);

            var grid = MakeRect("Grid", _window);
            _gridRt = grid.GetComponent<RectTransform>();
            _gridRt.anchorMin = _gridRt.anchorMax = new Vector2(0f, 1f);
            _gridRt.pivot = new Vector2(0f, 1f);
            _gridRt.anchoredPosition = new Vector2(0f, -HeaderH);
            AddImage(grid, new Color(0.059f, 0.071f, 0.067f, 1f));

            _gridTexture = BuildGridTexture();
            var linesGo = MakeRect("Lines", grid.transform);
            Stretch(linesGo.GetComponent<RectTransform>());
            _gridLines = linesGo.AddComponent<RawImage>();
            _gridLines.texture = _gridTexture;

            _emptyText = MakeTMP("Empty", grid.transform, 22f, FontStyles.Italic, TextAlignmentOptions.Center);
            _emptyText.text = "Nothing worth carrying.";
            _emptyText.color = Muted;
            Stretch(_emptyText.rectTransform);

            var more = MakeRect("More", _window);
            _moreRow = more.GetComponent<RectTransform>();
            _moreRow.anchorMin = new Vector2(0f, 0f); _moreRow.anchorMax = new Vector2(1f, 0f);
            _moreRow.pivot = new Vector2(0.5f, 0f);
            _moreRow.anchoredPosition = Vector2.zero; _moreRow.sizeDelta = new Vector2(0f, MoreH);
            more.AddComponent<CanvasGroup>();
            AddHairline(_moreRow, 0f);

            _moreLeft = MakeTMP("L", more.transform, 16f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            _moreLeft.color = Muted;
            Inset(_moreLeft.rectTransform, 16f, 0f);
            _moreRight = MakeTMP("R", more.transform, 16f, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
            _moreRight.color = Muted;
            Inset(_moreRight.rectTransform, 16f, 0f);
        }

        private void BuildSide()
        {
            _kickerText = SideLabel("Kicker", 0f, 26f, 18f, FontStyles.Bold);
            _kickerText.characterSpacing = 12f;

            _titleText = SideLabel("Title", TitleY, 86f, 70f, FontStyles.Normal);

            _labelText = SideLabel("Label", 156f, 22f, 15f, FontStyles.Normal);
            _labelText.characterSpacing = 8f;

            _totalText = SideLabel("Total", 180f, 124f, 120f, FontStyles.Normal);
            AddSoftShadow(_totalText);

            var strikeGo = MakeRect("Strike", _totalText.transform);
            _strike = strikeGo.GetComponent<RectTransform>();
            _strike.anchorMin = _strike.anchorMax = new Vector2(0f, 0.5f);
            _strike.pivot = new Vector2(0f, 0.5f);
            _strike.anchoredPosition = new Vector2(-7f, -4f);
            _strike.sizeDelta = new Vector2(0f, 7f);
            AddImage(strikeGo, Bad);

            var chipGo = MakeRect("Compare", _frame);
            _chip = chipGo.GetComponent<RectTransform>();
            TopLeft(_chip, SideLeft, GridTop + 316f, 300f, 38f);
            _chipCg = chipGo.AddComponent<CanvasGroup>();
            AddFrame(_chip, Gold);
            _chipText = MakeTMP("Text", chipGo.transform, 16f, FontStyles.Bold, TextAlignmentOptions.Center);
            _chipText.characterSpacing = 5f;
            _chipText.enableWordWrapping = false;
            Stretch(_chipText.rectTransform);

            var stats = MakeRect("Stats", _frame);
            var strt = stats.GetComponent<RectTransform>();
            TopLeft(strt, SideLeft, GridTop + 392f, SideWidth, 120f);
            _statsCg = stats.AddComponent<CanvasGroup>();
            AddHairline(strt, -1f, top: true);
            _pmcText  = StatColumn(strt, "PMC KILLS",  0);
            _scavText = StatColumn(strt, "SCAV KILLS", 1);
            _xpText   = StatColumn(strt, "EXPERIENCE", 2);
            _bonusText = MakeTMP("Bonus", stats.transform, 15f, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            _bonusText.enableWordWrapping = false;
            _bonusText.characterSpacing = 3f;
            TopLeft(_bonusText.rectTransform, 2f * SideWidth / 3f, 96f, SideWidth / 3f, 22f);

            var quip = MakeRect("Quip", _frame);
            var qrt = quip.GetComponent<RectTransform>();
            TopLeft(qrt, SideLeft, GridTop + 540f, SideWidth - 60f, 90f);
            _quipCg = quip.AddComponent<CanvasGroup>();
            _quipText = MakeTMP("Text", quip.transform, 28f, FontStyles.Italic, TextAlignmentOptions.TopLeft);
            _quipText.color = new Color(0.71f, 0.73f, 0.69f);
            _quipText.enableWordWrapping = true;
            Stretch(_quipText.rectTransform);

            BuildSquad();
        }

        /// PIT Fireteam block under the quip. Kept to the left half of the column so it never
        /// runs under the video panel in the bottom-right corner.
        private void BuildSquad()
        {
            const float w = 420f;
            var go = MakeRect("Fireteam", _frame);
            var rt = go.GetComponent<RectTransform>();
            TopLeft(rt, SideLeft, GridTop + 650f, w, 30f + MaxMateRows * MateRowH);
            _squadCg = go.AddComponent<CanvasGroup>();

            _squadHeader = MakeTMP("Header", go.transform, 14f, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            _squadHeader.color = Muted;
            _squadHeader.characterSpacing = 6f;
            _squadHeader.enableWordWrapping = false;
            TopLeft(_squadHeader.rectTransform, 0f, 0f, w, 20f);
            AddHairline(rt, -24f);

            for (int i = 0; i < MaxMateRows; i++)
            {
                var row = MakeRect($"Mate{i}", go.transform);
                var rrt = row.GetComponent<RectTransform>();
                TopLeft(rrt, 0f, 30f + i * MateRowH, w, MateRowH);
                var cg = row.AddComponent<CanvasGroup>();

                var name = MakeTMP("Name", row.transform, 19f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
                name.enableWordWrapping = false;
                name.overflowMode = TextOverflowModes.Ellipsis;
                name.rectTransform.anchorMin = Vector2.zero; name.rectTransform.anchorMax = new Vector2(0.6f, 1f);
                name.rectTransform.offsetMin = name.rectTransform.offsetMax = Vector2.zero;

                var detail = MakeTMP("Detail", row.transform, 15f, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
                detail.enableWordWrapping = false;
                detail.characterSpacing = 3f;
                detail.rectTransform.anchorMin = new Vector2(0.6f, 0f); detail.rectTransform.anchorMax = Vector2.one;
                detail.rectTransform.offsetMin = detail.rectTransform.offsetMax = Vector2.zero;

                _mateRows.Add(new MateRow { Cg = cg, Name = name, Detail = detail });
            }
        }

        private void ResetSquad(RaidStats stats)
        {
            var mates = stats.Fireteam;
            if (mates == null && stats.FireteamMembers != null)   // history from before 1.2
            {
                mates = new List<FireteamMate>();
                foreach (var (n, k) in stats.FireteamMembers) mates.Add(new FireteamMate { Name = n, Kills = k });
            }

            bool has = mates != null && mates.Count > 0;
            _squadCg.gameObject.SetActive(has);
            _squadCg.alpha = 0f;
            if (!has) return;

            int squadKills = 0, down = 0;
            foreach (var m in mates) { squadKills += m.Kills; if (!m.Alive) down++; }
            _squadHeader.text = $"FIRETEAM  ·  {squadKills} KILL{(squadKills == 1 ? "" : "S")}"
                              + (down > 0 ? $"  ·  {down} DOWN" : "");

            // The last row turns into a "+N more" line when the squad does not fit.
            int shown = mates.Count > MaxMateRows ? MaxMateRows - 1 : mates.Count;
            for (int i = 0; i < _mateRows.Count; i++)
            {
                var row = _mateRows[i];
                row.Cg.alpha = 0f;
                row.Cg.gameObject.SetActive(i < shown || (i == shown && mates.Count > MaxMateRows));
                if (i < shown)
                {
                    var m = mates[i];
                    row.Name.text    = m.Name;
                    row.Name.color   = m.Alive ? new Color(0.88f, 0.89f, 0.86f) : new Color(0.62f, 0.50f, 0.48f);
                    string kills     = m.Kills > 0 ? $"{m.Kills} KILL{(m.Kills == 1 ? "" : "S")}" : "NO KILLS";
                    row.Detail.text  = m.Alive ? kills : $"KIA  ·  {kills}";
                    row.Detail.color = m.Alive ? (m.Kills > 0 ? Gold : Muted) : Bad;
                }
                else if (i == shown)
                {
                    row.Name.text    = $"+{mates.Count - shown} more";
                    row.Name.color   = Muted;
                    row.Detail.text  = string.Empty;
                }
            }
        }

        private IEnumerator RevealSquad()
        {
            if (!_squadCg.gameObject.activeSelf) yield break;
            _squadCg.alpha = 1f;
            _squadHeader.color = new Color(Muted.r, Muted.g, Muted.b, 0f);
            yield return StartCoroutine(FadeText(_squadHeader, Muted, 0.35f));
            foreach (var row in _mateRows)
            {
                if (!row.Cg.gameObject.activeSelf) continue;
                LootSounds.Play(LootSounds.Tick, "MenuCheckBox");
                yield return StartCoroutine(FadeCanvasGroup(row.Cg, 0.25f));
            }
        }

        private TextMeshProUGUI StatColumn(RectTransform parent, string label, int col)
        {
            float w = SideWidth / 3f, x = col * w;
            var l = MakeTMP(label, parent, 14f, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            l.text = label;
            l.color = Muted;
            l.characterSpacing = 6f;
            l.enableWordWrapping = false;
            TopLeft(l.rectTransform, x, 20f, w, 20f);

            var v = MakeTMP(label + "Value", parent, 50f, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            v.color = new Color(0.93f, 0.94f, 0.92f);
            v.enableWordWrapping = false;
            TopLeft(v.rectTransform, x, 42f, w, 56f);
            return v;
        }

        private TextMeshProUGUI SideLabel(string name, float y, float h, float size, FontStyles style)
        {
            var t = MakeTMP(name, _frame, size, style, TextAlignmentOptions.TopLeft);
            t.color = Color.clear;
            t.enableWordWrapping = false;
            TopLeft(t.rectTransform, SideLeft, GridTop + y, SideWidth, h);
            return t;
        }

        private void BuildFoot()
        {
            var foot = MakeRect("Foot", _frame);
            var frt = foot.GetComponent<RectTransform>();
            frt.anchorMin = new Vector2(0f, 0f); frt.anchorMax = new Vector2(1f, 0f);
            frt.pivot = new Vector2(0.5f, 0f);
            frt.anchoredPosition = new Vector2(0f, 20f); frt.sizeDelta = new Vector2(-2f * GridLeft, 40f);
            _footCg = foot.AddComponent<CanvasGroup>();

            var brand = MakeTMP("Brand", foot.transform, 14f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            brand.text = "LOOTNET";
            brand.color = new Color(0.44f, 0.46f, 0.43f);
            brand.characterSpacing = 8f;
            Stretch(brand.rectTransform);

            _metaText = MakeTMP("Meta", foot.transform, 14f, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
            _metaText.color = new Color(0.44f, 0.46f, 0.43f);
            _metaText.characterSpacing = 6f;
            _metaText.enableWordWrapping = false;
            Stretch(_metaText.rectTransform);
        }

        private void BuildVideoPanel()
        {
            var vpGo = new GameObject("VideoPlayerHost");
            vpGo.transform.SetParent(transform, false);
            _videoPlayer = vpGo.AddComponent<VideoPlayer>();
            _videoPlayer.playOnAwake     = false;
            _videoPlayer.isLooping       = false;
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
            _videoPlayer.renderMode      = VideoRenderMode.RenderTexture;
            _videoPlayer.SetDirectAudioVolume(0, 0.35f);

            _videoPanel = MakeRect("VideoPanel", _frame);
            var rt = _videoPanel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot     = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-GridLeft, 80f);
            rt.sizeDelta = new Vector2(VideoMaxWidth, VideoMaxWidth * 9f / 16f);

            _videoCg = _videoPanel.AddComponent<CanvasGroup>();
            _videoCg.alpha = 0f;
            AddImage(_videoPanel, new Color(0f, 0f, 0f, 0.85f));

            var imgGo = MakeRect("VideoImage", _videoPanel.transform);
            var imgRt = imgGo.GetComponent<RectTransform>();
            imgRt.anchorMin = Vector2.zero; imgRt.anchorMax = Vector2.one;
            imgRt.offsetMin = new Vector2(3f, 3f); imgRt.offsetMax = new Vector2(-3f, -3f);
            _videoImage = imgGo.AddComponent<RawImage>();
        }

        private IEnumerator PlayVideoSequence()
        {
            if (!Plugin.VideoEnabled.Value) yield break;

            string path = Path.Combine(BepInEx.Paths.PluginPath, "LootNet", VideoFileName);
            if (!File.Exists(path))
            {
                Plugin.LogSource.LogWarning($"[LootNet] Video not found: {path}");
                yield break;
            }

            if (!_videoPlayer.isPrepared)
            {
                _videoPlayer.url = "file:///" + path.Replace("\\", "/");
                _videoPlayer.Prepare();
                float timeout = 8f;
                while (!_videoPlayer.isPrepared && timeout > 0f)
                {
                    timeout -= Time.deltaTime;
                    yield return null;
                }
                if (!_videoPlayer.isPrepared) yield break;
            }

            uint vw = _videoPlayer.width;
            uint vh = _videoPlayer.height;
            if (vw > 0 && vh > 0)
            {
                float h  = VideoMaxWidth * vh / vw;
                var   rt = _videoPanel.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(VideoMaxWidth, h);
            }

            if (_videoRenderTexture != null) Destroy(_videoRenderTexture);
            _videoRenderTexture = new RenderTexture((int)_videoPlayer.width, (int)_videoPlayer.height, 0);
            _videoPlayer.targetTexture = _videoRenderTexture;
            _videoImage.texture        = _videoRenderTexture;

            yield return StartCoroutine(FadeCanvasGroup(_videoCg, 0.6f));

            bool ended = false;
            _videoPlayer.loopPointReached += _ => ended = true;
            _videoPlayer.Play();

            while (!ended) yield return null;
            _videoPlayer.Pause();
        }

        private void OnDestroy()
        {
            if (_videoRenderTexture != null) Destroy(_videoRenderTexture);
            if (_vignetteTexture    != null) Destroy(_vignetteTexture);
            if (_dimTexture         != null) Destroy(_dimTexture);
            if (_scrimTexture       != null) Destroy(_scrimTexture);
            if (_gridTexture        != null) Destroy(_gridTexture);
            if (_bgBlurTexture      != null) Destroy(_bgBlurTexture);
        }

        // ---------- helpers ----------

        private static GameObject MakeRect(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            return go;
        }

        private static Image AddImage(GameObject go, Color color)
        {
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;   // cells are built after the click-catcher sweep
            return img;
        }

        private static TextMeshProUGUI MakeTMP(string name, Transform parent, float size, FontStyles style, TextAlignmentOptions align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.fontSize = size; t.fontStyle = style; t.alignment = align;
            t.raycastTarget = false;
            return t;
        }

        private static void TopLeft(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static void Inset(RectTransform rt, float x, float y)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(x, y); rt.offsetMax = new Vector2(-x, -y);
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero; rt.anchoredPosition = Vector2.zero;
        }

        /// One-pixel border drawn as four edge strips (UGUI has no outline for plain rects).
        private static void AddFrame(RectTransform rt, Color color, float t = 1f)
        {
            void Edge(string n, Vector2 min, Vector2 max, Vector2 size)
            {
                var go = MakeRect(n, rt);
                var e = go.GetComponent<RectTransform>();
                e.anchorMin = min; e.anchorMax = max;
                e.pivot = new Vector2(0.5f, 0.5f);
                e.anchoredPosition = Vector2.zero;
                e.sizeDelta = size;
                AddImage(go, color);
            }
            Edge("EdgeT", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, t));
            Edge("EdgeB", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, t));
            Edge("EdgeL", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(t, 0f));
            Edge("EdgeR", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(t, 0f));
        }

        private static void SetFrameColor(RectTransform rt, Color color)
        {
            foreach (Transform child in rt)
                if (child.name.StartsWith("Edge") && child.TryGetComponent<Image>(out var img))
                    img.color = color;
        }

        private static void AddHairline(RectTransform parent, float offset, bool top = true)
        {
            var go = MakeRect("Hairline", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, top ? 1f : 0f); rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, offset);
            rt.sizeDelta = new Vector2(0f, 1f);
            AddImage(go, Line);
        }

        /// Only for the big type. Underlay on the small labels just muddies them.
        private static void AddSoftShadow(TextMeshProUGUI t)
        {
            try
            {
                var mat = t.fontMaterial;           // instance, never sharedMaterial
                mat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
                mat.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.55f));
                mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX,   0.6f);
                mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY,  -0.6f);
                mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness,  0.35f);
                mat.SetFloat(ShaderUtilities.ID_UnderlayDilate,    0.1f);
            }
            catch (Exception e)
            {
                // Some cloned EFT font materials have no underlay pass.
                Plugin.LogSource.LogWarning($"[LootNet] Underlay unavailable: {e.Message}");
            }
        }

        private static string Rub(double v) => $"₽ {v:N0}";

        private static string ShortRub(double v)
        {
            if (v >= 1_000_000) return $"{v / 1_000_000:0.#}M";
            if (v >= 1_000)     return $"{Math.Round(v / 1_000):0}K";
            return v > 0 ? $"{v:0}" : "-";
        }

        private static Color ValueTint(double v)
        {
            if (v >= 500_000) return new Color(1f,    0.45f, 0.85f);
            if (v >= 300_000) return new Color(0.78f, 0.55f, 1f);
            if (v >= 150_000) return new Color(0.45f, 0.75f, 1f);
            if (v >= 50_000)  return new Color(0.90f, 0.78f, 0.30f);
            return new Color(0.72f, 0.72f, 0.72f);
        }

        /// Cell backgrounds follow the same tiers as the value text, much darker.
        private static Color CellTint(double v)
        {
            if (v >= 500_000) return new Color(0.42f, 0.17f, 0.36f, 0.75f);
            if (v >= 300_000) return new Color(0.32f, 0.22f, 0.42f, 0.75f);
            if (v >= 150_000) return new Color(0.17f, 0.28f, 0.39f, 0.75f);
            if (v >= 50_000)  return new Color(0.33f, 0.29f, 0.13f, 0.75f);
            return new Color(0.20f, 0.21f, 0.20f, 0.75f);
        }

        /// Bilinear downsample of the frozen frame. Cheap defocus so the type is not
        /// sitting on sharp game detail.
        private Texture Defocus(Texture2D source)
        {
            if (source == null) return null;

            int w = Mathf.Max(16, source.width  / 6);
            int h = Mathf.Max(16, source.height / 6);

            if (_bgBlurTexture != null) Destroy(_bgBlurTexture);
            _bgBlurTexture = new RenderTexture(w, h, 0) { filterMode = FilterMode.Bilinear };

            var prev = RenderTexture.active;
            Graphics.Blit(source, _bgBlurTexture);
            RenderTexture.active = prev;

            return _bgBlurTexture;
        }

        /// Horizontal dim: heaviest behind the grid and the side column, a touch lighter
        /// in the gap between them so the frozen frame still reads.
        private static Texture2D BuildDimTexture()
        {
            const int w = 64;
            var tex = new Texture2D(w, 1, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode   = TextureWrapMode.Clamp;
            var px = new Color32[w];
            for (int x = 0; x < w; x++)
            {
                float u = x / (w - 1f);
                float a = u < 0.55f ? Mathf.Lerp(0.84f, 0.72f, u / 0.55f) : Mathf.Lerp(0.72f, 0.86f, (u - 0.55f) / 0.45f);
                px[x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        /// Vertical scrim, heavy at the bottom. Tinted red on a death.
        private static Texture2D BuildScrimTexture()
        {
            const int h = 64;
            var tex = new Texture2D(1, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode   = TextureWrapMode.Clamp;
            var px = new Color32[h];
            for (int y = 0; y < h; y++)
            {
                float v = y / (h - 1f);                            // 0 = bottom of screen
                float a = Mathf.SmoothStep(1f, 0f, Mathf.Clamp01(v / 0.75f));
                px[y] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        /// One stash cell: a faint line on the top and left edge, tiled by uvRect.
        private static Texture2D BuildGridTexture()
        {
            const int s = 32;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode   = TextureWrapMode.Repeat;
            var px = new Color32[s * s];
            var line = new Color32(255, 255, 255, 14);
            var none = new Color32(255, 255, 255, 0);
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
                px[y * s + x] = (x == 0 || y == s - 1) ? line : none;
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        private static Texture2D BuildVignetteTexture()
        {
            const int s = 64;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode   = TextureWrapMode.Clamp;
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = (x / (s - 1f)) * 2f - 1f;
                float dy = (y / (s - 1f)) * 2f - 1f;
                float d  = Mathf.Sqrt(dx * dx + dy * dy);
                float a  = Mathf.Clamp01((d - 0.68f) / 0.55f);
                a        = a * a;
                px[y * s + x] = new Color32(0, 0, 0, (byte)(a * 180f));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
