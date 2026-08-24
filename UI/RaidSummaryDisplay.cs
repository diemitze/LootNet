using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Comfort.Common;
using EFT.UI;
using LootNet.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace LootNet.UI
{
    /// Cinema layout: letterbox bars over the frozen death/extract screen, typography
    /// in the lower third, loot as a quiet right-aligned list. No panels, no badges.
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
        private const float CountUpDuration    = 1.4f;
        private const float RowStagger         = 0.2f;

        private const float BarHeight   = 150f;
        private const float BodyBottom  = 320f;
        private const float BodyLeft    = 190f;
        private const float RuleLeft    = 150f;
        private const float RuleHeight  = 210f;
        private const float RowHeight   = 36f;
        private const int   MaxItemRows = 5;
        private const float UpperBandY  = 690f;   // the empty band between the loot column and the top bar
        private const float HeroSize    = 132f;

        private CanvasGroup     _canvasGroup;
        private RawImage        _bgImage;
        private RawImage        _tint;
        private GameObject      _root;

        private RectTransform   _barTop;
        private RectTransform   _barBottom;
        private RectTransform   _rule;

        private TextMeshProUGUI _kickerText;
        private TextMeshProUGUI _titleText;
        private TextMeshProUGUI _totalText;
        private TextMeshProUGUI _quipText;
        private TextMeshProUGUI _metaText;
        private TextMeshProUGUI _watermarkText;
        private TextMeshProUGUI _compareText;

        private RectTransform   _heroRoot;
        private CanvasGroup     _heroCg;
        private Image           _heroIcon;
        private TextMeshProUGUI _heroName;
        private TextMeshProUGUI _heroValue;
        private CanvasGroup     _quipCg;

        private RectTransform   _sideColumn;
        private readonly List<SideRow> _itemRows = new();
        private SideRow         _summaryRow;
        private SideRow         _bonusRow;
        private SideRow         _fireteamRow;

        private RectTransform   _progressBarFill;
        private Texture2D       _vignetteTexture;
        private Texture2D       _scrimTexture;
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

        private struct SideRow
        {
            public CanvasGroup      Cg;
            public RectTransform    Rt;
            public TextMeshProUGUI  Left;
            public TextMeshProUGUI  Right;
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
            LootIconCache.RetryFailed();

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

            float t = 0f;
            while (t < FadeInDuration)
            {
                t += Time.deltaTime;
                _canvasGroup.alpha = Mathf.SmoothStep(0f, 1f, t / FadeInDuration);
                yield return null;
            }
            _canvasGroup.alpha = 1f;

            LootSounds.Play(LootSounds.Bars, "MenuOpenContainer");
            StartCoroutine(GrowBars(0.65f));
            StartCoroutine(FadeImage(_tint, 1f, 0.9f));
            StartCoroutine(FadeText(_watermarkText, new Color(1f, 1f, 1f, 0.05f), 1.4f));

            yield return new WaitForSeconds(0.35f);
            StartCoroutine(GrowRule(0.5f));

            yield return new WaitForSeconds(0.2f);
            yield return StartCoroutine(FadeText(_kickerText, new Color(0.54f, 0.54f, 0.54f), 0.6f));

            yield return new WaitForSeconds(0.1f);
            yield return StartCoroutine(RiseText(_titleText, TitleColor(died), 12f, 0.7f));

            yield return new WaitForSeconds(0.2f);
            Color gold = new Color(1f, 0.84f, 0f);
            StartCoroutine(FadeText(_totalText, gold, 0.5f));
            yield return StartCoroutine(CountUp(_totalText, stats.TotalFoundValue, CountUpDuration));
            LootSounds.Play(LootSounds.Total, "MenuCheckBox");

            if (!string.IsNullOrEmpty(_compareText.text))
            {
                float cx = Mathf.Min(BodyLeft + _totalText.preferredWidth + 34f, 1040f);
                _compareText.rectTransform.anchoredPosition = new Vector2(cx, BodyBottom + 14f);
                StartCoroutine(FadeText(_compareText, new Color(0.85f, 0.72f, 0.25f), 0.7f));
            }

            int rowCount = Mathf.Min(stats.TopItems.Count, MaxItemRows);
            for (int i = 0; i < rowCount; i++)
            {
                yield return StartCoroutine(FadeInRow(_itemRows[i], 0.35f));
                LootSounds.Play(LootSounds.Tick, "MenuCheckBox");
                yield return new WaitForSeconds(RowStagger);
            }

            StartCoroutine(FadeInRow(_summaryRow, 0.5f));
            StartCoroutine(WatchForXpBonus(stats));

            if (_heroRoot != null && _heroRoot.gameObject.activeSelf)
            {
                StartCoroutine(ResolveHeroIcon(stats));
                StartCoroutine(FadeCanvasGroup(_heroCg, 0.8f));
            }

            if (_fireteamRow.Cg != null && _fireteamRow.Rt.gameObject.activeSelf)
            {
                yield return new WaitForSeconds(0.2f);
                StartCoroutine(FadeInRow(_fireteamRow, 0.5f));
            }

            StartCoroutine(PlayVideoSequence());

            yield return new WaitForSeconds(0.55f);

            if (_quipCg != null && !string.IsNullOrEmpty(_quipText.text))
            {
                LootSounds.Play(died ? LootSounds.ResolveDied : LootSounds.ResolveSurvived, "MenuCheckBox");
                yield return StartCoroutine(FadeCanvasGroup(_quipCg, 0.9f));
            }

            yield return new WaitForSeconds(0.2f);
            yield return StartCoroutine(FadeText(_metaText, new Color(0.36f, 0.36f, 0.36f), 0.6f));
        }

        private void ResetHero(RaidStats stats)
        {
            _heroCg.alpha = 0f;

            bool has = stats.TopItems != null && stats.TopItems.Count > 0;
            _heroRoot.gameObject.SetActive(has);
            if (!has) return;

            var (name, value) = stats.TopItems[0];
            _heroName.text   = name;
            _heroValue.text  = value > 0 ? $"₽{value:N0}" : "-";
            _heroValue.color = ValueTint(value);

            _heroIcon.enabled = false;
            _heroIcon.sprite  = null;

            string tpl = HeroTemplate(stats);
            if (tpl != null) LootIconCache.Request(tpl);
        }

        private static string HeroTemplate(RaidStats stats)
        {
            var t = stats.TopItemTemplates;
            return t != null && t.Count > 0 && !string.IsNullOrEmpty(t[0]) ? t[0] : null;
        }

        /// Icons stream in from the game's bundles, so give it a moment before giving up.
        /// The block still reads fine as text alone.
        private IEnumerator ResolveHeroIcon(RaidStats stats)
        {
            string tpl = HeroTemplate(stats);
            if (tpl == null) yield break;

            float wait = 2f;
            while (wait > 0f && LootIconCache.Get(tpl) == null && LootIconCache.IsPending(tpl))
            {
                wait -= Time.deltaTime;
                yield return null;
            }

            var sprite = LootIconCache.Get(tpl);
            if (sprite == null || _heroIcon == null) yield break;

            _heroIcon.sprite  = sprite;
            _heroIcon.enabled = true;
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
            if (rank == 1) return "BEST RAID YET";
            if (rank <= 3) return $"{Ordinal(rank)} BEST OF YOUR LAST {total}";

            double sum = 0;
            foreach (var v in prior) sum += v;
            double avg = sum / prior.Count;
            if (avg <= 0) return null;

            int pct = Mathf.RoundToInt((float)((value - avg) / avg * 100.0));
            if (pct == 0) return "DEAD ON YOUR AVERAGE";
            return pct > 0 ? $"+{pct}% VS YOUR AVERAGE" : $"{pct}% VS YOUR AVERAGE";
        }

        private static string Ordinal(int n) => n == 2 ? "2ND" : n == 3 ? "3RD" : $"{n}TH";

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

            if (bonusXp <= 0 || _bonusRow.Cg == null) yield break;

            string label = stats.PlayerSurvived ? "survived bonus" : "extract bonus";
            _bonusRow.Left.text  = $"incl. +{bonusXp:N0} {label}";
            _bonusRow.Right.text = string.Empty;
            _bonusRow.Rt.gameObject.SetActive(true);

            int finalXp = baseXp + bonusXp;
            float dur = 0.8f, t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float eased = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f);
                _summaryRow.Right.text = $"+{Mathf.RoundToInt(Mathf.Lerp(baseXp, finalXp, eased)):N0} XP";
                if (_bonusRow.Cg != null) _bonusRow.Cg.alpha = eased;
                yield return null;
            }
            _summaryRow.Right.text = $"+{finalXp:N0} XP";
            if (_bonusRow.Cg != null) _bonusRow.Cg.alpha = 1f;
        }

        private void ResetUI(RaidStats stats)
        {
            bool died = !stats.PlayerSurvived;

            _kickerText.text  = died ? "EXTRACTION FAILED!" : "EXTRACTION COMPLETE";
            _kickerText.color = Color.clear;

            _titleText.text  = died ? "You did not make it out." : "You made it out.";
            _titleText.color = Color.clear;

            _totalText.text  = "₽ 0";
            _totalText.color = Color.clear;

            _quipText.text = Quips.Pick(stats);
            _quipCg.alpha  = 0f;

            string map = string.IsNullOrEmpty(stats.MapName) ? "UNKNOWN" : stats.MapName.ToUpperInvariant();

            string time = stats.RaidTime == default ? DateTime.Now.ToString("HH:mm") : stats.RaidTime.ToString("HH:mm");
            _metaText.text  = $"{map}  ·  {time}  ·  " + (Plugin.ManualDismiss.Value ? "CLICK TO CONTINUE" : "CLICK ANYWHERE TO CLOSE");
            _metaText.color = Color.clear;

            _watermarkText.text  = map;
            _watermarkText.color = Color.clear;

            _compareText.text  = BuildComparison(stats) ?? string.Empty;
            _compareText.color = Color.clear;
            _compareText.rectTransform.anchoredPosition = new Vector2(BodyLeft, BodyBottom + 14f);

            ResetHero(stats);

            _barTop.sizeDelta    = new Vector2(0f, 0f);
            _barBottom.sizeDelta = new Vector2(0f, 0f);
            _rule.sizeDelta      = new Vector2(3f, 0f);
            _tint.color          = new Color(0.016f, 0.024f, 0.04f, 0f);

            int rowCount = Mathf.Min(stats.TopItems.Count, MaxItemRows);
            for (int i = 0; i < _itemRows.Count; i++)
            {
                bool used = i < rowCount;
                _itemRows[i].Rt.gameObject.SetActive(used);
                if (!used) continue;

                var (name, value) = stats.TopItems[i];
                _itemRows[i].Left.text   = name;
                _itemRows[i].Right.text  = value > 0 ? $"₽{value:N0}" : "-";
                _itemRows[i].Right.color = ValueTint(value);
                _itemRows[i].Cg.alpha    = 0f;
            }

            _summaryRow.Left.text  = $"{stats.ItemsFound} items  ·  {stats.PmcKills} PMC  ·  {stats.ScavKills} Scav";
            _summaryRow.Right.text = stats.XpEarned > 0 ? $"+{stats.XpEarned:N0} XP" : string.Empty;
            _summaryRow.Cg.alpha   = 0f;

            _bonusRow.Left.text  = string.Empty;
            _bonusRow.Cg.alpha   = 0f;
            _bonusRow.Rt.gameObject.SetActive(false);

            bool hasFireteam = stats.FireteamMembers != null && stats.FireteamMembers.Count > 0;
            _fireteamRow.Rt.gameObject.SetActive(hasFireteam);
            if (hasFireteam)
            {
                var names = new List<string>();
                foreach (var (n, k) in stats.FireteamMembers)
                {
                    names.Add(k > 0 ? $"{n} ({k})" : n);
                    if (names.Count >= 4) break;
                }
                _fireteamRow.Left.text  = "fireteam";
                _fireteamRow.Right.text = string.Join("  ·  ", names);
                _fireteamRow.Cg.alpha   = 0f;
            }

            if (_progressBarFill != null)
            {
                _progressBarFill.anchorMax = new Vector2(1f, 1f);
                _progressBarFill.transform.parent.gameObject.SetActive(!Plugin.ManualDismiss.Value);
            }

            if (_videoPlayer != null) { _videoPlayer.Pause(); _videoPlayer.time = 0; }
            if (_videoCg     != null) _videoCg.alpha = 0f;
        }

        private IEnumerator GrowBars(float dur)
        {
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float p = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f);
                float h = BarHeight * p;
                _barTop.sizeDelta    = new Vector2(0f, h);
                _barBottom.sizeDelta = new Vector2(0f, h);
                yield return null;
            }
            _barTop.sizeDelta    = new Vector2(0f, BarHeight);
            _barBottom.sizeDelta = new Vector2(0f, BarHeight);
        }

        private IEnumerator GrowRule(float dur)
        {
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float p = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f);
                _rule.sizeDelta = new Vector2(3f, RuleHeight * p);
                yield return null;
            }
            _rule.sizeDelta = new Vector2(3f, RuleHeight);
        }

        private static IEnumerator FadeImage(Graphic img, float targetAlpha, float dur)
        {
            Color c = img.color;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                img.color = new Color(c.r, c.g, c.b, Mathf.Lerp(0f, targetAlpha, t / dur));
                yield return null;
            }
            img.color = new Color(c.r, c.g, c.b, targetAlpha);
        }

        private static IEnumerator CountUp(TextMeshProUGUI label, double target, float dur)
        {
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float p = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f);
                label.text = $"₽ {target * p:N0}";
                yield return null;
            }
            label.text = $"₽ {target:N0}";
        }

        private static IEnumerator FadeInRow(SideRow row, float dur)
        {
            var endPos   = row.Rt.anchoredPosition;
            var startPos = endPos + new Vector2(14f, 0f);
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float p = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f);
                row.Rt.anchoredPosition = Vector2.Lerp(startPos, endPos, p);
                row.Cg.alpha = Mathf.Clamp01(t / dur * 1.6f);
                yield return null;
            }
            row.Rt.anchoredPosition = endPos;
            row.Cg.alpha = 1f;
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

        private void BuildUI()
        {
            var canvas = gameObject.GetComponent<Canvas>() ?? gameObject.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;

            var scaler = gameObject.GetComponent<CanvasScaler>() ?? gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight  = 0.5f;

            if (!gameObject.GetComponent<GraphicRaycaster>()) gameObject.AddComponent<GraphicRaycaster>();
            _canvasGroup = gameObject.GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

            _root = MakeRect("SummaryRoot", transform);
            Stretch(_root.GetComponent<RectTransform>());

            var screenshotGo = MakeRect("Screenshot", _root.transform);
            Stretch(screenshotGo.GetComponent<RectTransform>());
            _bgImage = screenshotGo.AddComponent<RawImage>();
            _bgImage.color = new Color(0.80f, 0.84f, 0.95f, 1f);

            _scrimTexture = BuildScrimTexture();
            var tintGo = MakeRect("Tint", _root.transform);
            Stretch(tintGo.GetComponent<RectTransform>());
            _tint = tintGo.AddComponent<RawImage>();
            _tint.texture = _scrimTexture;
            _tint.color = new Color(0.016f, 0.024f, 0.04f, 0f);

            _vignetteTexture = BuildVignetteTexture();
            var vigGo = MakeRect("Vignette", _root.transform);
            Stretch(vigGo.GetComponent<RectTransform>());
            var vigImg = vigGo.AddComponent<RawImage>();
            vigImg.texture = _vignetteTexture;
            vigImg.color   = new Color(1f, 1f, 1f, 0.38f);

            BuildWatermark();

            _barTop    = MakeBar("BarTop",    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
            _barBottom = MakeBar("BarBottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));

            var ruleGo = MakeRect("Rule", _root.transform);
            _rule = ruleGo.GetComponent<RectTransform>();
            _rule.anchorMin = Vector2.zero; _rule.anchorMax = Vector2.zero;
            _rule.pivot = new Vector2(0f, 0f);
            _rule.anchoredPosition = new Vector2(RuleLeft, BodyBottom);
            _rule.sizeDelta = new Vector2(3f, 0f);
            ruleGo.AddComponent<Image>().color = new Color(1f, 0.84f, 0f, 0.9f);

            _kickerText = MakeBodyLabel("Kicker", 15f, FontStyles.Normal, BodyBottom + RuleHeight - 24f, 22f);
            _kickerText.characterSpacing = 8f;

            _titleText = MakeBodyLabel("Title", 48f, FontStyles.Normal, BodyBottom + 104f, 62f);
            AddSoftShadow(_titleText);

            _totalText = MakeBodyLabel("Total", 74f, FontStyles.Normal, BodyBottom, 90f);
            _totalText.characterSpacing = 1f;
            AddSoftShadow(_totalText);

            var quipGo = MakeRect("Quip", _root.transform);
            var quipRt = quipGo.GetComponent<RectTransform>();
            quipRt.anchorMin = Vector2.zero; quipRt.anchorMax = new Vector2(1f, 0f);
            quipRt.pivot = new Vector2(0f, 1f);
            quipRt.anchoredPosition = new Vector2(BodyLeft, BodyBottom - 12f);
            quipRt.sizeDelta = new Vector2(-(BodyLeft + 700f), 34f);
            _quipCg = quipGo.AddComponent<CanvasGroup>();
            _quipCg.alpha = 0f;

            _quipText = MakeTMP("QuipText", quipGo.transform, 22f, FontStyles.Italic, TextAlignmentOptions.TopLeft);
            _quipText.color = new Color(0.66f, 0.66f, 0.66f);
            _quipText.enableWordWrapping = false;
            Stretch(_quipText.rectTransform);

            _metaText = MakeBodyLabel("Meta", 13f, FontStyles.Normal, 66f, 20f);
            _metaText.characterSpacing = 5f;

            var colGo = MakeRect("SideColumn", _root.transform);
            _sideColumn = colGo.GetComponent<RectTransform>();
            _sideColumn.anchorMin = new Vector2(1f, 0f); _sideColumn.anchorMax = new Vector2(1f, 0f);
            _sideColumn.pivot     = new Vector2(1f, 0f);
            _sideColumn.anchoredPosition = new Vector2(-BodyLeft, BodyBottom);
            _sideColumn.sizeDelta = new Vector2(620f, 320f);

            for (int i = 0; i < MaxItemRows; i++)
                _itemRows.Add(BuildSideRow($"Item{i}", -i * RowHeight, 20f, 20f, new Color(0.82f, 0.82f, 0.82f), Color.white));

            AddHairline(_sideColumn, -(MaxItemRows * RowHeight) - 14f);

            _summaryRow  = BuildSideRow("Summary", -(MaxItemRows * RowHeight) - 30f, 17f, 19f,
                                        new Color(0.6f, 0.6f, 0.6f), new Color(0.85f, 0.72f, 0.25f));
            _bonusRow    = BuildSideRow("Bonus", -(MaxItemRows * RowHeight) - 58f, 14f, 14f,
                                        new Color(0.45f, 0.55f, 0.45f), new Color(0.45f, 0.55f, 0.45f));
            _fireteamRow = BuildSideRow("Fireteam", -(MaxItemRows * RowHeight) - 88f, 14f, 14f,
                                        new Color(0.42f, 0.42f, 0.42f), new Color(0.62f, 0.62f, 0.62f));

            BuildCompareLine();
            BuildHero();

            var progTrack = MakeRect("ProgressTrack", _root.transform);
            var ptRt = progTrack.GetComponent<RectTransform>();
            ptRt.anchorMin = new Vector2(0f, 0f); ptRt.anchorMax = new Vector2(1f, 0f);
            ptRt.pivot = new Vector2(0.5f, 0f);
            ptRt.anchoredPosition = Vector2.zero; ptRt.sizeDelta = new Vector2(0f, 2f);
            progTrack.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.06f);

            var progFillGo = MakeRect("ProgressFill", progTrack.transform);
            _progressBarFill = progFillGo.GetComponent<RectTransform>();
            _progressBarFill.anchorMin = Vector2.zero; _progressBarFill.anchorMax = Vector2.one;
            _progressBarFill.sizeDelta = Vector2.zero; _progressBarFill.anchoredPosition = Vector2.zero;
            _progressBarFill.pivot = new Vector2(0f, 0.5f);
            progFillGo.AddComponent<Image>().color = new Color(1f, 0.84f, 0f, 0.45f);

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

        /// Map name as a ghosted title-card watermark. Auto-sizes down so a long map name
        /// never runs under the hero block on the right.
        private void BuildWatermark()
        {
            _watermarkText = MakeTMP("Watermark", _root.transform, 150f, FontStyles.Normal, TextAlignmentOptions.BottomLeft);
            _watermarkText.color = Color.clear;
            _watermarkText.enableWordWrapping = false;
            _watermarkText.characterSpacing   = 14f;
            _watermarkText.enableAutoSizing   = true;
            _watermarkText.fontSizeMin        = 60f;
            _watermarkText.fontSizeMax        = 150f;

            var rt = _watermarkText.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(BodyLeft - 6f, UpperBandY);
            rt.sizeDelta = new Vector2(-(BodyLeft + 700f), 180f);
        }

        private void BuildCompareLine()
        {
            _compareText = MakeTMP("Compare", _root.transform, 16f, FontStyles.Normal, TextAlignmentOptions.BottomLeft);
            _compareText.color = Color.clear;
            _compareText.enableWordWrapping = false;
            _compareText.characterSpacing   = 4f;

            var rt = _compareText.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(BodyLeft, BodyBottom + 14f);   // x is set once the total stops counting
            rt.sizeDelta = new Vector2(-(BodyLeft + 620f), 24f);
        }

        /// The single best find, given the icon treatment, opposite the watermark.
        private void BuildHero()
        {
            var go = MakeRect("Hero", _root.transform);
            _heroRoot = go.GetComponent<RectTransform>();
            _heroRoot.anchorMin = new Vector2(1f, 0f); _heroRoot.anchorMax = new Vector2(1f, 0f);
            _heroRoot.pivot     = new Vector2(1f, 0f);
            _heroRoot.anchoredPosition = new Vector2(-BodyLeft, UpperBandY);
            _heroRoot.sizeDelta = new Vector2(460f, HeroSize);
            _heroCg = go.AddComponent<CanvasGroup>();
            _heroCg.alpha = 0f;

            var iconGo = MakeRect("HeroIcon", go.transform);
            var iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.anchorMin = new Vector2(1f, 0.5f); iconRt.anchorMax = new Vector2(1f, 0.5f);
            iconRt.pivot     = new Vector2(1f, 0.5f);
            iconRt.anchoredPosition = Vector2.zero;
            iconRt.sizeDelta = new Vector2(HeroSize, HeroSize);
            _heroIcon = iconGo.AddComponent<Image>();
            _heroIcon.preserveAspect = true;
            _heroIcon.enabled = false;

            var label = MakeTMP("HeroLabel", go.transform, 12f, FontStyles.Normal, TextAlignmentOptions.TopRight);
            label.text = "TOP FIND";
            label.color = new Color(0.42f, 0.42f, 0.42f);
            label.characterSpacing = 6f;
            label.enableWordWrapping = false;
            var lrt = label.rectTransform;
            lrt.anchorMin = new Vector2(0f, 1f); lrt.anchorMax = new Vector2(1f, 1f);
            lrt.pivot = new Vector2(0f, 1f);
            lrt.anchoredPosition = new Vector2(0f, -18f);
            lrt.sizeDelta = new Vector2(-(HeroSize + 22f), 18f);

            _heroName = MakeTMP("HeroName", go.transform, 23f, FontStyles.Normal, TextAlignmentOptions.TopRight);
            _heroName.color = new Color(0.88f, 0.88f, 0.88f);
            _heroName.enableWordWrapping = false;
            _heroName.overflowMode = TextOverflowModes.Ellipsis;
            var nrt = _heroName.rectTransform;
            nrt.anchorMin = new Vector2(0f, 1f); nrt.anchorMax = new Vector2(1f, 1f);
            nrt.pivot = new Vector2(0f, 1f);
            nrt.anchoredPosition = new Vector2(0f, -46f);
            nrt.sizeDelta = new Vector2(-(HeroSize + 22f), 30f);

            _heroValue = MakeTMP("HeroValue", go.transform, 21f, FontStyles.Normal, TextAlignmentOptions.TopRight);
            _heroValue.enableWordWrapping = false;
            var vrt = _heroValue.rectTransform;
            vrt.anchorMin = new Vector2(0f, 1f); vrt.anchorMax = new Vector2(1f, 1f);
            vrt.pivot = new Vector2(0f, 1f);
            vrt.anchoredPosition = new Vector2(0f, -80f);
            vrt.sizeDelta = new Vector2(-(HeroSize + 22f), 28f);
        }

        private RectTransform MakeBar(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
        {
            var go = MakeRect(name, _root.transform);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, 0f);
            go.AddComponent<Image>().color = Color.black;
            return rt;
        }

        private TextMeshProUGUI MakeBodyLabel(string name, float size, FontStyles style, float bottomY, float height)
        {
            var label = MakeTMP(name, _root.transform, size, style, TextAlignmentOptions.BottomLeft);
            label.color = Color.clear;
            label.enableWordWrapping = false;
            var rt = label.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(BodyLeft, bottomY);
            rt.sizeDelta = new Vector2(-(BodyLeft + 700f), height);
            return label;
        }

        private SideRow BuildSideRow(string name, float topOffset, float leftSize, float rightSize, Color leftColor, Color rightColor)
        {
            var go = MakeRect($"Row_{name}", _sideColumn);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, topOffset);
            rt.sizeDelta = new Vector2(0f, RowHeight - 4f);

            var cg = go.AddComponent<CanvasGroup>();
            cg.alpha = 0f;

            var left = MakeTMP("L", go.transform, leftSize, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            left.color = leftColor;
            left.enableWordWrapping = false;
            left.overflowMode = TextOverflowModes.Ellipsis;
            var lr = left.rectTransform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = new Vector2(0.62f, 1f);
            lr.offsetMin = Vector2.zero; lr.offsetMax = Vector2.zero;

            var right = MakeTMP("R", go.transform, rightSize, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
            right.color = rightColor;
            right.enableWordWrapping = false;
            var rr = right.rectTransform;
            rr.anchorMin = new Vector2(0.62f, 0f); rr.anchorMax = Vector2.one;
            rr.offsetMin = Vector2.zero; rr.offsetMax = Vector2.zero;

            return new SideRow { Cg = cg, Rt = rt, Left = left, Right = right };
        }

        private static void AddHairline(Transform parent, float topOffset)
        {
            var go = MakeRect("Hairline", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, topOffset);
            rt.sizeDelta = new Vector2(0f, 1f);
            go.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.09f);
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

            _videoPanel = MakeRect("VideoPanel", _root.transform);
            var rt = _videoPanel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot     = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-BodyLeft, BarHeight + 40f);
            rt.sizeDelta = new Vector2(VideoMaxWidth, VideoMaxWidth * 9f / 16f);

            _videoCg = _videoPanel.AddComponent<CanvasGroup>();
            _videoCg.alpha = 0f;
            _videoPanel.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);

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
            if (_scrimTexture       != null) Destroy(_scrimTexture);
            if (_bgBlurTexture      != null) Destroy(_bgBlurTexture);
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
            t.fontSize = size; t.fontStyle = style; t.alignment = align;
            return t;
        }

        /// Only for the big type. Underlay on the 14px side rows just muddies them.
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

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero; rt.anchoredPosition = Vector2.zero;
        }

        private static Color TitleColor(bool died) =>
            died ? new Color(1f, 0.54f, 0.54f) : Color.white;

        private static Color ValueTint(double v)
        {
            if (v >= 500_000) return new Color(1f,    0.45f, 0.85f);
            if (v >= 300_000) return new Color(0.78f, 0.55f, 1f);
            if (v >= 150_000) return new Color(0.45f, 0.75f, 1f);
            if (v >= 50_000)  return new Color(0.90f, 0.78f, 0.30f);
            return new Color(0.72f, 0.72f, 0.72f);
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

        /// Vertical scrim: near-clear at the top, heavy across the lower band where all
        /// the typography lives. A uniform tint reads flat, a gradient reads lit.
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
                float a = Mathf.SmoothStep(0.90f, 0.10f, Mathf.Clamp01(v / 0.70f));
                px[y] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
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
