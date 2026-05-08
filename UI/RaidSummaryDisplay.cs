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

        private const float AutoDismissSeconds = 14f;
        private const float FadeInDuration     = 0.45f;
        private const float CountUpDuration    = 2.2f;
        private const float StaggerDelay       = 0.25f;
        private const float RowHeight          = 52f;
        private const float SlideDistance      = 60f;
        private const float RowSlideDuration   = 0.28f;

        private CanvasGroup         _canvasGroup;
        private RawImage            _bgImage;
        private Image               _scanLine;
        private GameObject          _root;
        private RectTransform       _panel;
        private TextMeshProUGUI     _titleText;
        private TextMeshProUGUI     _subtitleText;
        private TextMeshProUGUI     _valueText;

        // Left-column stat labels (show final value) and counter labels (count up)
        private TextMeshProUGUI     _statItemsNum;
        private TextMeshProUGUI     _statPmcNum;
        private TextMeshProUGUI     _statScavNum;
        private CanvasGroup         _statItemsCg;
        private CanvasGroup         _statPmcCg;
        private CanvasGroup         _statScavCg;

        private TextMeshProUGUI     _dismissText;
        private RectTransform       _progressBarFill;
        private Transform           _topItemsContainer;
        private Image               _valuePulseRing;
        private GameObject          _fireteamSection;
        private Transform           _fireteamContainer;
        private Texture2D           _vignetteTexture;

        // Column divider that draws itself downward
        private RectTransform       _colDivider;

        private GameObject          _videoPanel;
        private CanvasGroup         _videoCg;
        private RawImage            _videoImage;
        private VideoPlayer         _videoPlayer;
        private RenderTexture       _videoRenderTexture;
        private const float         VideoMaxWidth = 320f;
        private const string        VideoFileName = "weii weii.mp4";

        private readonly List<ItemRowData> _itemRows     = new();
        private readonly List<ItemRowData> _fireteamRows = new();

        private float      _timer;
        private bool       _visible;
        private Coroutine  _scanLineCoroutine;

        private struct ItemRowData
        {
            public CanvasGroup      Cg;
            public RectTransform    Rt;
            public TextMeshProUGUI  Label;
            public TextMeshProUGUI  RankBadge;
            public Image            RankBg;
            public Image            AccentBar;
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
            _timer -= Time.deltaTime;
            float frac = Mathf.Clamp01(_timer / AutoDismissSeconds);
            if (_progressBarFill != null)
                _progressBarFill.anchorMax = new Vector2(frac, 1f);
            if (_dismissText != null)
            {
                int secs = Mathf.Max(0, Mathf.CeilToInt(_timer));
                _dismissText.text = $"Click anywhere to close  ·  {secs}s";
            }
            if (_timer <= 0f) Hide();
        }


        public void QueueSummary(RaidStats stats)
        {
            if (_visible) return;
            StopAllCoroutines();
            StartCoroutine(CaptureAndShow(stats));
        }

        private IEnumerator CaptureAndShow(RaidStats stats)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            if (_bgImage != null) _bgImage.texture = tex;
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
            _visible = false;
            StartCoroutine(FadeOutAndReveal());
        }

        private IEnumerator FadeOutAndReveal()
        {
            const float fadeDur = 0.5f;
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

            PlaySound("BackpackOpen");

            // Fade panel in
            float t = 0f;
            while (t < FadeInDuration)
            {
                t += Time.deltaTime;
                _canvasGroup.alpha = Mathf.SmoothStep(0f, 1f, t / FadeInDuration);
                yield return null;
            }
            _canvasGroup.alpha = 1f;

            // Panel slides up slightly
            if (_panel != null)
            {
                var startPos = _panel.anchoredPosition + new Vector2(0f, -40f);
                _panel.anchoredPosition = startPos;
                t = 0f;
                const float slideDur = 0.35f;
                while (t < slideDur)
                {
                    t += Time.deltaTime;
                    float p = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / slideDur), 3f);
                    _panel.anchoredPosition = Vector2.Lerp(startPos, startPos + new Vector2(0f, 40f), p);
                    yield return null;
                }
                _panel.anchoredPosition = startPos + new Vector2(0f, 40f);
            }

            PlaySound("AchievementCompleted");
            yield return StartCoroutine(SlideInText(_titleText, SlideDistance, 0.35f));
            StartCoroutine(FlashTitle(_titleText));
            yield return new WaitForSeconds(0.05f);
            yield return StartCoroutine(SlideInText(_subtitleText, SlideDistance * 0.5f, 0.25f));

            yield return new WaitForSeconds(0.1f);

            // Column divider draws down
            StartCoroutine(DrawDivider(0.45f));

            // Value text appears
            Color valueColor = ValueColor(stats.TotalFoundValue);
            _valueText.color = new Color(valueColor.r, valueColor.g, valueColor.b, 0f);
            yield return StartCoroutine(FadeText(_valueText, new Color(valueColor.r, valueColor.g, valueColor.b), 0.2f));
            _valueText.text = "₽ 0";

            // Stat rows slide in and count up
            StartCoroutine(SlideInStatRow(_statItemsCg, _statItemsNum, stats.ItemsFound, 0.05f, 0.5f, 1.2f));
            StartCoroutine(SlideInStatRow(_statPmcCg,   _statPmcNum,   stats.PmcKills,  0.30f, 0.5f, 0.9f));
            StartCoroutine(SlideInStatRow(_statScavCg,  _statScavNum,  stats.ScavKills, 0.55f, 0.5f, 0.7f));

            // Prepare item rows
            int rowCount = Mathf.Min(stats.TopItems.Count, 7);
            EnsureItemRows(rowCount);
            PrepareItemRows(stats);

            // Reveal rows with stagger; value counter increments per row (+200ms after each slide starts)
            double runningVal  = 0;
            double totalVal    = stats.TotalFoundValue;
            Color  vc          = ValueColor(totalVal);

            for (int i = 0; i < rowCount; i++)
            {
                int   idx = i;
                double itemVal = stats.TopItems[i].Value;
                runningVal += itemVal;
                double targetVal = runningVal;
                bool   isLast    = i == rowCount - 1;

                RevealRow(idx, itemVal);

                // Increment the total value counter 200ms after this row starts sliding
                StartCoroutine(BumpValue(targetVal, totalVal, vc, 0.20f, isLast));

                yield return new WaitForSeconds(StaggerDelay);
            }

            StartCoroutine(PlayVideoSequence());

            yield return new WaitForSeconds(0.15f);

            if (_fireteamSection != null && _fireteamSection.activeSelf)
            {
                yield return new WaitForSeconds(0.1f);
                for (int i = 0; i < _fireteamRows.Count; i++)
                {
                    if (i < stats.FireteamMembers?.Count)
                        StartCoroutine(SlideInRow(_fireteamRows[i], 0.2f));
                    yield return new WaitForSeconds(StaggerDelay);
                }
            }

            yield return new WaitForSeconds(0.3f);
            PlaySound("MenuEscape");
            yield return StartCoroutine(FadeText(_dismissText, new Color(0.4f, 0.4f, 0.4f), 0.3f));

            _scanLineCoroutine = StartCoroutine(AnimateScanLine());
        }

        // Bumps the value counter to targetVal over dur seconds, then pulses if it's the last item
        private IEnumerator BumpValue(double targetVal, double totalVal, Color vc, float delay, bool isLast)
        {
            yield return new WaitForSeconds(delay);

            double startVal = 0;
            if (double.TryParse(_valueText.text.Replace("₽ ", "").Replace(",", ""), out double parsed))
                startVal = parsed;

            float t   = 0f;
            float dur = 0.26f;
            while (t < dur)
            {
                t += Time.deltaTime;
                double v = startVal + (targetVal - startVal) * Mathf.Clamp01(t / dur);
                _valueText.text = $"₽ {v:N0}";
                yield return null;
            }
            _valueText.text = $"₽ {targetVal:N0}";

            if (isLast)
            {
                _valueText.text = $"₽ {totalVal:N0}";
                PlaySound("QuestStarted");
                StartCoroutine(PulseScale(_valueText.rectTransform, 1.08f, 0.25f));
                if (_valuePulseRing != null) StartCoroutine(PulseRing(_valuePulseRing, 0.5f));
            }
        }

        private IEnumerator SlideInStatRow(CanvasGroup cg, TextMeshProUGUI numLabel, int target, float delay, float slideDur, float countDur)
        {
            yield return new WaitForSeconds(delay);

            PlaySound("MenuCheckBox");

            var rt       = cg.GetComponent<RectTransform>();
            var startPos = rt.anchoredPosition - new Vector2(20f, 0f);
            rt.anchoredPosition = startPos;
            cg.alpha = 0f;

            float t = 0f;
            while (t < slideDur)
            {
                t += Time.deltaTime;
                float p = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / slideDur), 3f);
                rt.anchoredPosition = Vector2.Lerp(startPos, startPos + new Vector2(20f, 0f), p);
                cg.alpha = Mathf.Clamp01(t / slideDur * 2f);
                yield return null;
            }
            cg.alpha = 1f;

            // Count up
            t = 0f;
            while (t < countDur)
            {
                t += Time.deltaTime;
                int v = Mathf.RoundToInt(target * Mathf.Clamp01(t / countDur));
                numLabel.text = v.ToString();
                yield return null;
            }
            numLabel.text = target.ToString();
        }

        private IEnumerator DrawDivider(float dur)
        {
            if (_colDivider == null) yield break;
            _colDivider.gameObject.SetActive(true);
            PlaySound("MenuOpenContainer");
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float p = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f);
                _colDivider.anchorMax = new Vector2(_colDivider.anchorMax.x, 1f - p);
                yield return null;
            }
            _colDivider.anchorMax = new Vector2(_colDivider.anchorMax.x, 0f);
        }

        private void ResetUI(RaidStats stats)
        {
            _titleText.text     = "RAID COMPLETE";
            _titleText.color    = new Color(1f, 0.84f, 0f, 0f);
            _subtitleText.text  = "LOOT SUMMARY";
            _subtitleText.color = new Color(0.55f, 0.55f, 0.55f, 0f);
            _valueText.text     = "₽ 0";
            _valueText.color    = Color.clear;
            _dismissText.text   = string.Empty;
            _dismissText.color  = Color.clear;

            if (_statItemsNum != null) _statItemsNum.text = "0";
            if (_statPmcNum   != null) _statPmcNum.text   = "0";
            if (_statScavNum  != null) _statScavNum.text  = "0";
            if (_statItemsCg  != null) _statItemsCg.alpha = 0f;
            if (_statPmcCg    != null) _statPmcCg.alpha   = 0f;
            if (_statScavCg   != null) _statScavCg.alpha  = 0f;

            if (_colDivider != null)
            {
                _colDivider.anchorMax = new Vector2(_colDivider.anchorMax.x, 1f);
                _colDivider.gameObject.SetActive(false);
            }

            bool hasFireteam = stats.FireteamMembers != null && stats.FireteamMembers.Count > 0;
            if (_fireteamSection != null)
            {
                _fireteamSection.SetActive(hasFireteam);
                if (hasFireteam)
                {
                    int count = Mathf.Min(stats.FireteamMembers.Count, 4);
                    var fsRt  = _fireteamSection.GetComponent<RectTransform>();
                    fsRt.sizeDelta = new Vector2(-40f, 28f + RowHeight * count + 6f);
                    EnsureFireteamRows(count);
                    PrepareFireteamRows(stats.FireteamMembers);
                }
            }

            if (_progressBarFill != null)
                _progressBarFill.anchorMax = new Vector2(1f, 1f);

            foreach (var r in _itemRows)
            {
                r.Cg.alpha = 0f;
                r.Rt.anchoredPosition = new Vector2(-SlideDistance, r.Rt.anchoredPosition.y);
            }
            foreach (var r in _fireteamRows)
            {
                r.Cg.alpha = 0f;
                r.Rt.anchoredPosition = new Vector2(-SlideDistance, r.Rt.anchoredPosition.y);
            }

            if (_videoPlayer != null) { _videoPlayer.Pause(); _videoPlayer.time = 0; }
            if (_videoCg     != null) _videoCg.alpha = 0f;
        }

        private void PrepareItemRows(RaidStats stats)
        {
            int count = Mathf.Min(stats.TopItems.Count, 7);
            for (int i = 0; i < count; i++)
            {
                var (name, value) = stats.TopItems[i];
                Color rarityColor = RarityColor(value);
                string priceStr   = value > 0 ? $"₽{value:N0}" : "—";
                string rarityHex  = ColorUtility.ToHtmlStringRGB(rarityColor);

                if (i < _itemRows.Count)
                {
                    var row = _itemRows[i];
                    row.Label.text     = $"<color=#{rarityHex}>{name}</color>  <color=#555555>{priceStr}</color>";
                    row.RankBadge.text = RankLabel(i);
                    row.RankBg.color   = RankColor(i);
                    row.AccentBar.color = rarityColor;
                    row.Cg.alpha = 0f;
                }
            }
        }

        private void EnsureFireteamRows(int count)
        {
            while (_fireteamRows.Count < count)
                BuildFireteamRow(_fireteamRows.Count);
        }

        private void PrepareFireteamRows(List<(string Name, int Kills)> members)
        {
            int count = Mathf.Min(members.Count, 4);
            for (int i = 0; i < count; i++)
            {
                var (name, kills) = members[i];
                Color accent = kills >= 3 ? new Color(0.85f, 0.35f, 0.35f)
                             : kills >= 1 ? new Color(0.55f, 0.65f, 0.55f)
                             :              new Color(0.35f, 0.35f, 0.35f);
                string killStr = kills == 1 ? "1 kill" : $"{kills} kills";
                var row = _fireteamRows[i];
                row.Label.text     = $"{name}  <color=#555555>{killStr}</color>";
                row.RankBadge.text = kills > 0 ? $"×{kills}" : "—";
                row.RankBg.color   = new Color(accent.r * 0.4f, accent.g * 0.4f, accent.b * 0.4f, 1f);
                row.AccentBar.color = accent;
                row.Cg.alpha = 0f;
            }
        }

        private void BuildFireteamRow(int index)
        {
            var row = MakeRect($"TeamRow{index}", _fireteamContainer);
            var rr  = row.GetComponent<RectTransform>();
            rr.anchorMin = new Vector2(0f, 1f); rr.anchorMax = new Vector2(1f, 1f);
            rr.pivot     = new Vector2(0f, 1f);
            rr.anchoredPosition = new Vector2(0f, -index * RowHeight);
            rr.sizeDelta = new Vector2(0f, RowHeight - 4f);

            var cg = row.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            row.AddComponent<Image>().color = new Color(0.05f, 0.07f, 0.05f, index % 2 == 0 ? 0.6f : 0.3f);

            var accentGo  = MakeRect("Accent", row.transform);
            var accentRt  = accentGo.GetComponent<RectTransform>();
            accentRt.anchorMin = Vector2.zero; accentRt.anchorMax = new Vector2(0f, 1f);
            accentRt.pivot = new Vector2(0f, 0.5f);
            accentRt.anchoredPosition = Vector2.zero; accentRt.sizeDelta = new Vector2(3f, 0f);
            var accentImg = accentGo.AddComponent<Image>();

            const float badgeW = 36f;
            var badgeGo = MakeRect("KillBadge", row.transform);
            var badgeRt = badgeGo.GetComponent<RectTransform>();
            badgeRt.anchorMin = new Vector2(0f, 0.5f); badgeRt.anchorMax = new Vector2(0f, 0.5f);
            badgeRt.pivot = new Vector2(0f, 0.5f);
            badgeRt.anchoredPosition = new Vector2(8f, 0f);
            badgeRt.sizeDelta = new Vector2(badgeW, 22f);
            var badgeBg = badgeGo.AddComponent<Image>();

            var badgeLabel = MakeTMP($"KillLabel{index}", badgeGo.transform, 11f, FontStyles.Bold, TextAlignmentOptions.Center);
            badgeLabel.color = new Color(0.9f, 0.9f, 0.9f);
            Stretch(badgeLabel.rectTransform);

            var label = MakeTMP($"TeamLabel{index}", row.transform, 15f, FontStyles.Normal, TextAlignmentOptions.Left);
            label.color = new Color(0.85f, 0.85f, 0.85f);
            var lr = label.rectTransform;
            lr.anchorMin = new Vector2(0f, 0f); lr.anchorMax = new Vector2(1f, 1f);
            lr.pivot = new Vector2(0f, 0.5f);
            lr.offsetMin = new Vector2(8f + badgeW + 12f, 0f);
            lr.offsetMax = new Vector2(-8f, 0f);

            _fireteamRows.Add(new ItemRowData
            {
                Cg = cg, Rt = rr, Label = label, RankBadge = badgeLabel, RankBg = badgeBg, AccentBar = accentImg
            });
        }

        private void RevealRow(int i, double value = 0)
        {
            if (i >= _itemRows.Count) return;
            var row = _itemRows[i];
            StartCoroutine(SlideInRow(row, RowSlideDuration));

            if      (value >= 500_000) PlaySound("QuestCompleted",       "RepairComplete");
            else if (value >= 150_000) PlaySound("MenuInstallModVital",   "RepairComplete");
            else                       PlaySound("InsuranceItemOnInsure", "ButtonClick");
        }

        private static IEnumerator SlideInRow(ItemRowData row, float dur)
        {
            var startPos = row.Rt.anchoredPosition;
            var endPos   = startPos + new Vector2(SlideDistance, 0f);
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float p = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f);
                row.Rt.anchoredPosition = Vector2.Lerp(startPos, endPos, p);
                row.Cg.alpha = Mathf.Clamp01(t / dur * 2f);
                yield return null;
            }
            row.Rt.anchoredPosition = endPos;
            row.Cg.alpha = 1f;
        }

        private static IEnumerator SlideInText(TextMeshProUGUI label, float slideAmt, float dur)
        {
            var rt       = label.rectTransform;
            var startPos = rt.anchoredPosition - new Vector2(0f, slideAmt);
            rt.anchoredPosition = startPos;
            Color target = new Color(label.color.r, label.color.g, label.color.b);
            label.color = new Color(target.r, target.g, target.b, 0f);
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float p = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f);
                rt.anchoredPosition = Vector2.Lerp(startPos, startPos + new Vector2(0f, slideAmt), p);
                label.color = new Color(target.r, target.g, target.b, Mathf.Clamp01(t / dur * 1.5f));
                yield return null;
            }
            rt.anchoredPosition = startPos + new Vector2(0f, slideAmt);
            label.color = target;
        }

        private static IEnumerator FadeInCanvasGroup(CanvasGroup cg, float dur)
        {
            float t = 0f;
            while (t < dur) { t += Time.deltaTime; cg.alpha = Mathf.Clamp01(t / dur); yield return null; }
            cg.alpha = 1f;
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

        private static IEnumerator PulseScale(RectTransform rt, float peakScale, float dur)
        {
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float p = Mathf.Sin(Mathf.Clamp01(t / dur) * Mathf.PI);
                rt.localScale = Vector3.one * Mathf.Lerp(1f, peakScale, p);
                yield return null;
            }
            rt.localScale = Vector3.one;
        }

        private static IEnumerator PulseRing(Image ring, float dur)
        {
            ring.gameObject.SetActive(true);
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float p = t / dur;
                ring.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.5f, 2.2f, p);
                ring.color = new Color(1f, 0.84f, 0f, Mathf.Lerp(0.6f, 0f, p));
                yield return null;
            }
            ring.gameObject.SetActive(false);
        }

        private static IEnumerator FlashTitle(TextMeshProUGUI label)
        {
            Color gold = new Color(1f, 0.84f, 0f);
            float dur  = 0.45f;
            float t    = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float p     = t / dur;
                float flash = p < 0.25f ? p / 0.25f : Mathf.Pow(1f - (p - 0.25f) / 0.75f, 2f);
                label.color = Color.Lerp(gold, Color.white, flash * 0.75f);
                yield return null;
            }
            label.color = gold;
        }

        private IEnumerator AnimateScanLine()
        {
            if (_scanLine == null) yield break;
            var rt = _scanLine.rectTransform;
            while (_visible)
            {
                float t = 0f;
                const float sweep = 3f;
                while (t < sweep && _visible)
                {
                    t += Time.deltaTime;
                    float p = t / sweep;
                    rt.anchorMin = new Vector2(0f, 1f - p);
                    rt.anchorMax = new Vector2(1f, 1f - p);
                    _scanLine.color = new Color(1f, 1f, 1f, 0.025f * Mathf.Sin(p * Mathf.PI));
                    yield return null;
                }
                yield return new WaitForSeconds(1.5f);
            }
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
            rt.anchoredPosition = new Vector2(-24f, 24f);
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

            yield return StartCoroutine(FadeInCanvasGroup(_videoCg, 0.6f));

            bool ended = false;
            _videoPlayer.loopPointReached += _ => ended = true;
            _videoPlayer.Play();

            while (!ended) yield return null;
            _videoPlayer.Pause();
        }

        private void OnDestroy()
        {
            if (_videoRenderTexture != null) Destroy(_videoRenderTexture);
        }


        private void BuildUI()
        {
            var canvas = gameObject.GetComponent<Canvas>() ?? gameObject.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            if (!gameObject.GetComponent<CanvasScaler>())     gameObject.AddComponent<CanvasScaler>();
            if (!gameObject.GetComponent<GraphicRaycaster>()) gameObject.AddComponent<GraphicRaycaster>();
            _canvasGroup = gameObject.GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

            _root = MakeRect("SummaryRoot", transform);
            Stretch(_root.GetComponent<RectTransform>());

            // Background screenshot
            var screenshotGo = MakeRect("Screenshot", _root.transform);
            Stretch(screenshotGo.GetComponent<RectTransform>());
            _bgImage = screenshotGo.AddComponent<RawImage>();
            _bgImage.color = new Color(1f, 1f, 1f, 0.35f);

            // Dark overlay
            var overlay = MakeRect("Overlay", _root.transform);
            Stretch(overlay.GetComponent<RectTransform>());
            overlay.AddComponent<Image>().color = new Color(0.01f, 0.01f, 0.03f, 0.82f);

            // Vignette
            _vignetteTexture = BuildVignetteTexture();
            var vigGo = MakeRect("Vignette", _root.transform);
            Stretch(vigGo.GetComponent<RectTransform>());
            var vigImg = vigGo.AddComponent<RawImage>();
            vigImg.texture = _vignetteTexture;
            vigImg.color   = new Color(1f, 1f, 1f, 0.9f);

            // Scan line
            var scanGo = MakeRect("ScanLine", _root.transform);
            var scanRt = scanGo.GetComponent<RectTransform>();
            scanRt.anchorMin = new Vector2(0f, 1f); scanRt.anchorMax = new Vector2(1f, 1f);
            scanRt.pivot = Vector2.up; scanRt.sizeDelta = new Vector2(0f, 3f);
            _scanLine = scanGo.AddComponent<Image>();
            _scanLine.color = Color.clear;

            // Click-to-dismiss
            var clickGo = MakeRect("ClickCatcher", _root.transform);
            Stretch(clickGo.GetComponent<RectTransform>());
            clickGo.AddComponent<Image>().color = Color.clear;
            var btn = clickGo.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(Hide);

            // Gold accent bars
            MakeAccentBar("TopAccent", _root.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), 4f, new Color(1f, 0.84f, 0f, 1f));
            MakeAccentBar("BotAccent", _root.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), 2f, new Color(1f, 0.84f, 0f, 0.35f));

            // ── Main panel (wide) ──
            const float PanelW  = 1160f;
            const float PanelH  = 700f;

            var panelGo = MakeRect("Panel", _root.transform);
            _panel = panelGo.GetComponent<RectTransform>();
            _panel.anchorMin = new Vector2(0.5f, 0.5f); _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot     = new Vector2(0.5f, 0.5f);
            _panel.anchoredPosition = Vector2.zero;
            _panel.sizeDelta = new Vector2(PanelW, PanelH);

            panelGo.AddComponent<Image>().color = new Color(0.04f, 0.04f, 0.07f, 0.92f);

            // Left gold stripe
            var stripe   = MakeRect("LeftStripe", panelGo.transform);
            var stripeRt = stripe.GetComponent<RectTransform>();
            stripeRt.anchorMin = Vector2.zero; stripeRt.anchorMax = new Vector2(0f, 1f);
            stripeRt.pivot = new Vector2(0f, 0.5f);
            stripeRt.anchoredPosition = Vector2.zero; stripeRt.sizeDelta = new Vector2(4f, 0f);
            stripe.AddComponent<Image>().color = new Color(1f, 0.84f, 0f, 1f);

            // ── Header (full width) ──
            const float HeaderH = 140f;

            _titleText = MakeTMP("Title", panelGo.transform, 52f, FontStyles.Bold, TextAlignmentOptions.Center);
            _titleText.color = new Color(1f, 0.84f, 0f, 0f);
            _titleText.characterSpacing = 10f;
            PlaceLabel(_titleText.rectTransform, -32f, 72f);

            _subtitleText = MakeTMP("Subtitle", panelGo.transform, 13f, FontStyles.Normal, TextAlignmentOptions.Center);
            _subtitleText.color = new Color(0.55f, 0.55f, 0.55f, 0f);
            _subtitleText.characterSpacing = 5f;
            PlaceLabel(_subtitleText.rectTransform, -98f, 22f);

            // Header rule
            AddHRule(panelGo.transform, -HeaderH, new Color(1f, 0.84f, 0f, 0.3f));

            // ── Column divider (draws downward) ──
            var divGo = MakeRect("ColDivider", panelGo.transform);
            _colDivider = divGo.GetComponent<RectTransform>();
            _colDivider.anchorMin = new Vector2(0.5f, 0f);
            _colDivider.anchorMax = new Vector2(0.5f, 1f); // starts full height; DrawDivider shrinks anchorMax.y from 1→0
            _colDivider.pivot     = new Vector2(0.5f, 1f);
            _colDivider.offsetMin = new Vector2(-1f, 0f);
            _colDivider.offsetMax = new Vector2( 1f, -HeaderH);
            divGo.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.07f);
            divGo.SetActive(false);

            // ── LEFT COLUMN ──
            const float ColPad = 48f;
            const float BodyTop = -HeaderH - 8f;

            // Value label
            _valueText = MakeTMP("Value", panelGo.transform, 72f, FontStyles.Bold, TextAlignmentOptions.Left);
            _valueText.color = Color.clear;
            var vrt = _valueText.rectTransform;
            vrt.anchorMin = new Vector2(0f, 1f); vrt.anchorMax = new Vector2(0.5f, 1f);
            vrt.pivot = new Vector2(0f, 1f);
            vrt.anchoredPosition = new Vector2(ColPad, BodyTop - 20f);
            vrt.sizeDelta = new Vector2(-ColPad, 90f);

            // Pulse ring behind value
            var ringGo = MakeRect("PulseRing", panelGo.transform);
            var ringRt = ringGo.GetComponent<RectTransform>();
            ringRt.anchorMin = new Vector2(0f, 1f); ringRt.anchorMax = new Vector2(0.5f, 1f);
            ringRt.pivot = new Vector2(0f, 1f);
            ringRt.anchoredPosition = new Vector2(ColPad + 20f, BodyTop - 30f);
            ringRt.sizeDelta = new Vector2(280f, 80f);
            _valuePulseRing = ringGo.AddComponent<Image>();
            _valuePulseRing.color = Color.clear;
            ringGo.SetActive(false);

            // Value sublabel
            var valLabel = MakeTMP("ValueLabel", panelGo.transform, 10f, FontStyles.Normal, TextAlignmentOptions.Left);
            valLabel.text  = "TOTAL VALUE EXTRACTED";
            valLabel.color = new Color(0.3f, 0.3f, 0.3f, 1f);
            valLabel.characterSpacing = 3f;
            var vlRt = valLabel.rectTransform;
            vlRt.anchorMin = new Vector2(0f, 1f); vlRt.anchorMax = new Vector2(0.5f, 1f);
            vlRt.pivot = new Vector2(0f, 1f);
            vlRt.anchoredPosition = new Vector2(ColPad, BodyTop - 112f);
            vlRt.sizeDelta = new Vector2(-ColPad, 18f);

            // Stat rows
            float statY = BodyTop - 148f;
            BuildStatRow(panelGo.transform, "items looted", new Color(1f, 0.84f, 0f),         ColPad, statY,         out _statItemsNum, out _statItemsCg, new Color(1f, 0.84f, 0f));
            BuildStatRow(panelGo.transform, "PMC kills",    new Color(1f, 0.27f, 0.27f),       ColPad, statY - 64f,   out _statPmcNum,   out _statPmcCg,   new Color(1f, 0.27f, 0.27f));
            BuildStatRow(panelGo.transform, "scav kills",   new Color(0.55f, 0.55f, 0.55f),    ColPad, statY - 128f,  out _statScavNum,  out _statScavCg,  new Color(0.55f, 0.55f, 0.55f));

            // ── RIGHT COLUMN ──
            var hdr = MakeTMP("TopFindsHeader", panelGo.transform, 11f, FontStyles.Bold, TextAlignmentOptions.Center);
            hdr.text = "TOP FINDS";
            hdr.color = new Color(0.35f, 0.35f, 0.35f, 1f);
            hdr.characterSpacing = 4f;
            var hdrRt = hdr.rectTransform;
            hdrRt.anchorMin = new Vector2(0.5f, 1f); hdrRt.anchorMax = new Vector2(1f, 1f);
            hdrRt.pivot = new Vector2(0.5f, 1f);
            hdrRt.anchoredPosition = new Vector2(0f, BodyTop - 14f);
            hdrRt.sizeDelta = new Vector2(0f, 18f);

            var tcGo = MakeRect("TopItemsContainer", panelGo.transform);
            var tcRt = tcGo.GetComponent<RectTransform>();
            tcRt.anchorMin = new Vector2(0.5f, 1f); tcRt.anchorMax = new Vector2(1f, 1f);
            tcRt.pivot = new Vector2(0.5f, 1f);
            tcRt.anchoredPosition = new Vector2(0f, BodyTop - 40f);
            tcRt.sizeDelta = new Vector2(-16f, RowHeight * 7f);
            _topItemsContainer = tcGo.transform;

            for (int i = 0; i < 7; i++)
                BuildItemRow(i);

            // Fireteam section (below full width)
            float teamY = BodyTop - 40f - RowHeight * 7f - 10f;
            _fireteamSection = MakeRect("TeamSection", panelGo.transform);
            var fsSectionRt = _fireteamSection.GetComponent<RectTransform>();
            fsSectionRt.anchorMin = new Vector2(0f, 1f); fsSectionRt.anchorMax = new Vector2(1f, 1f);
            fsSectionRt.pivot     = new Vector2(0.5f, 1f);
            fsSectionRt.anchoredPosition = new Vector2(0f, teamY);
            fsSectionRt.sizeDelta = new Vector2(-40f, 0f);

            AddHRule(_fireteamSection.transform, 0f, new Color(0.25f, 0.25f, 0.25f, 0.5f));

            var teamHdr = MakeTMP("TeamHeader", _fireteamSection.transform, 11f, FontStyles.Bold, TextAlignmentOptions.Center);
            teamHdr.text = "TEAMMATES"; teamHdr.color = new Color(0.35f, 0.35f, 0.35f, 1f);
            teamHdr.characterSpacing = 4f;
            var thRt = teamHdr.rectTransform;
            thRt.anchorMin = new Vector2(0f, 1f); thRt.anchorMax = new Vector2(1f, 1f);
            thRt.pivot = new Vector2(0.5f, 1f);
            thRt.anchoredPosition = new Vector2(0f, -6f); thRt.sizeDelta = new Vector2(0f, 18f);

            var tcTeamGo = MakeRect("TeamContainer", _fireteamSection.transform);
            var tcTeamRt = tcTeamGo.GetComponent<RectTransform>();
            tcTeamRt.anchorMin = new Vector2(0f, 1f); tcTeamRt.anchorMax = new Vector2(1f, 1f);
            tcTeamRt.pivot = new Vector2(0.5f, 1f);
            tcTeamRt.anchoredPosition = new Vector2(0f, -28f);
            tcTeamRt.sizeDelta = new Vector2(0f, RowHeight * 4f);
            _fireteamContainer = tcTeamGo.transform;
            _fireteamSection.SetActive(false);

            // Progress bar
            var progTrack = MakeRect("ProgressTrack", panelGo.transform);
            var ptRt = progTrack.GetComponent<RectTransform>();
            ptRt.anchorMin = new Vector2(0f, 0f); ptRt.anchorMax = new Vector2(1f, 0f);
            ptRt.pivot = new Vector2(0.5f, 0f);
            ptRt.anchoredPosition = Vector2.zero; ptRt.sizeDelta = new Vector2(0f, 3f);
            progTrack.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 1f);

            var progFillGo = MakeRect("ProgressFill", progTrack.transform);
            _progressBarFill = progFillGo.GetComponent<RectTransform>();
            _progressBarFill.anchorMin = Vector2.zero; _progressBarFill.anchorMax = Vector2.one;
            _progressBarFill.sizeDelta = Vector2.zero; _progressBarFill.anchoredPosition = Vector2.zero;
            _progressBarFill.pivot = new Vector2(0f, 0.5f);
            progFillGo.AddComponent<Image>().color = new Color(1f, 0.84f, 0f, 0.65f);

            // Dismiss text
            _dismissText = MakeTMP("Dismiss", _root.transform, 12f, FontStyles.Normal, TextAlignmentOptions.Center);
            _dismissText.color = new Color(0.4f, 0.4f, 0.4f, 0f);
            var dr = _dismissText.rectTransform;
            dr.anchorMin = new Vector2(0f, 0f); dr.anchorMax = new Vector2(1f, 0f);
            dr.pivot = new Vector2(0.5f, 0f);
            dr.anchoredPosition = new Vector2(0f, 12f); dr.sizeDelta = new Vector2(0f, 20f);

            _canvasGroup.alpha = 0f;
            _root.SetActive(false);

            BuildVideoPanel();
        }

        private void BuildStatRow(Transform parent, string label, Color accentColor, float leftPad, float offsetY,
            out TextMeshProUGUI numLabel, out CanvasGroup cg, Color numColor)
        {
            var rowGo = MakeRect($"Stat_{label}", parent);
            var rowRt = rowGo.GetComponent<RectTransform>();
            rowRt.anchorMin = new Vector2(0f, 1f); rowRt.anchorMax = new Vector2(0.5f, 1f);
            rowRt.pivot = new Vector2(0f, 1f);
            rowRt.anchoredPosition = new Vector2(leftPad, offsetY);
            rowRt.sizeDelta = new Vector2(-leftPad - 8f, 52f);

            cg = rowGo.AddComponent<CanvasGroup>();
            cg.alpha = 0f;

            // Accent bar
            var accentGo = MakeRect("Accent", rowGo.transform);
            var accentRt = accentGo.GetComponent<RectTransform>();
            accentRt.anchorMin = Vector2.zero; accentRt.anchorMax = new Vector2(0f, 1f);
            accentRt.pivot = new Vector2(0f, 0.5f);
            accentRt.anchoredPosition = Vector2.zero; accentRt.sizeDelta = new Vector2(3f, 0f);
            accentGo.AddComponent<Image>().color = accentColor;

            // Number
            numLabel = MakeTMP($"Num_{label}", rowGo.transform, 28f, FontStyles.Bold, TextAlignmentOptions.Left);
            numLabel.color = numColor;
            var nRt = numLabel.rectTransform;
            nRt.anchorMin = new Vector2(0f, 0f); nRt.anchorMax = new Vector2(0f, 1f);
            nRt.pivot = new Vector2(0f, 0.5f);
            nRt.anchoredPosition = new Vector2(10f, 0f);
            nRt.sizeDelta = new Vector2(56f, 0f);

            // Label text
            var txt = MakeTMP($"Lbl_{label}", rowGo.transform, 14f, FontStyles.Normal, TextAlignmentOptions.Left);
            txt.text  = label;
            txt.color = new Color(0.6f, 0.6f, 0.6f, 1f);
            var tRt = txt.rectTransform;
            tRt.anchorMin = new Vector2(0f, 0f); tRt.anchorMax = new Vector2(1f, 1f);
            tRt.pivot = new Vector2(0f, 0.5f);
            tRt.offsetMin = new Vector2(72f, 0f);
            tRt.offsetMax = new Vector2(-4f, 0f);
        }

        private void BuildItemRow(int index)
        {
            var row = MakeRect($"ItemRow{index}", _topItemsContainer);
            var rr = row.GetComponent<RectTransform>();
            rr.anchorMin = new Vector2(0f, 1f); rr.anchorMax = new Vector2(1f, 1f);
            rr.pivot = new Vector2(0f, 1f);
            rr.anchoredPosition = new Vector2(0f, -index * RowHeight);
            rr.sizeDelta = new Vector2(0f, RowHeight - 4f);

            var cg = row.AddComponent<CanvasGroup>();
            cg.alpha = 0f;

            row.AddComponent<Image>().color = new Color(0.06f, 0.06f, 0.09f, index % 2 == 0 ? 0.6f : 0.3f);

            var accentGo = MakeRect("Accent", row.transform);
            var accentRt = accentGo.GetComponent<RectTransform>();
            accentRt.anchorMin = Vector2.zero; accentRt.anchorMax = new Vector2(0f, 1f);
            accentRt.pivot = new Vector2(0f, 0.5f);
            accentRt.anchoredPosition = Vector2.zero; accentRt.sizeDelta = new Vector2(3f, 0f);
            var accentImg = accentGo.AddComponent<Image>();

            const float badgeW = 36f;
            var badgeGo = MakeRect("RankBadge", row.transform);
            var badgeRt = badgeGo.GetComponent<RectTransform>();
            badgeRt.anchorMin = new Vector2(0f, 0.5f); badgeRt.anchorMax = new Vector2(0f, 0.5f);
            badgeRt.pivot = new Vector2(0f, 0.5f);
            badgeRt.anchoredPosition = new Vector2(8f, 0f);
            badgeRt.sizeDelta = new Vector2(badgeW, 22f);
            var badgeBg = badgeGo.AddComponent<Image>();

            var badgeLabel = MakeTMP($"RankLabel{index}", badgeGo.transform, 11f, FontStyles.Bold, TextAlignmentOptions.Center);
            badgeLabel.color = new Color(0.05f, 0.05f, 0.05f);
            Stretch(badgeLabel.rectTransform);

            var label = MakeTMP($"Label{index}", row.transform, 14f, FontStyles.Normal, TextAlignmentOptions.Left);
            label.color = new Color(0.9f, 0.9f, 0.9f);
            var lr = label.rectTransform;
            lr.anchorMin = new Vector2(0f, 0f); lr.anchorMax = new Vector2(1f, 1f);
            lr.pivot = new Vector2(0f, 0.5f);
            lr.offsetMin = new Vector2(8f + badgeW + 12f, 0f);
            lr.offsetMax = new Vector2(-8f, 0f);

            _itemRows.Add(new ItemRowData
            {
                Cg = cg, Rt = rr, Label = label, RankBadge = badgeLabel,
                RankBg = badgeBg, AccentBar = accentImg
            });
        }

        private void EnsureItemRows(int count)
        {
            while (_itemRows.Count < count)
                BuildItemRow(_itemRows.Count);
        }


        private static void MakeAccentBar(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, float h, Color color)
        {
            var go = MakeRect(name, parent);
            go.AddComponent<Image>().color = color;
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = anchorMin; r.anchorMax = anchorMax;
            r.pivot = anchorMin.y >= 1f ? Vector2.up : Vector2.zero;
            r.anchoredPosition = Vector2.zero; r.sizeDelta = new Vector2(0f, h);
        }

        private static void PlaceLabel(RectTransform rt, float offsetY, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, offsetY);
            rt.sizeDelta = new Vector2(-32f, height);
        }

        private static void AddHRule(Transform parent, float offsetY, Color color)
        {
            var go = MakeRect("HRule", parent);
            go.AddComponent<Image>().color = color;
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0.02f, 1f); r.anchorMax = new Vector2(0.98f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, offsetY); r.sizeDelta = new Vector2(0f, 1f);
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

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero; rt.anchoredPosition = Vector2.zero;
        }

        private static string RankLabel(int i) => i switch { 0 => "#1", 1 => "#2", 2 => "#3", _ => $"#{i+1}" };

        private static Color RankColor(int i) => i switch
        {
            0 => new Color(1f,    0.84f, 0f,    1f),
            1 => new Color(0.75f, 0.75f, 0.75f, 1f),
            2 => new Color(0.80f, 0.50f, 0.20f, 1f),
            _ => new Color(0.20f, 0.20f, 0.22f, 1f),
        };

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
                float a  = Mathf.Clamp01((d - 0.5f) / 0.65f);
                a        = a * a;
                px[y * s + x] = new Color32(0, 0, 0, (byte)(a * 210f));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        private static Color RarityColor(double v)
        {
            if (v >= 1_000_000) return new Color(1f,    0.15f, 0.15f);
            if (v >= 500_000)   return new Color(1f,    0.20f, 0.80f);
            if (v >= 300_000)   return new Color(0.75f, 0.30f, 1f);
            if (v >= 150_000)   return new Color(0.20f, 0.70f, 1f);
            if (v >= 50_000)    return new Color(1f,    0.85f, 0.10f);
            return new Color(0.40f, 0.40f, 0.40f);
        }

        private static Color ValueColor(double v)
        {
            if (v >= 1_000_000) return new Color(1f,    0.20f, 0.20f);
            if (v >= 500_000)   return new Color(1f,    0f,    1f);
            if (v >= 300_000)   return new Color(1f,    0.40f, 0.80f);
            if (v >= 150_000)   return new Color(0.40f, 0.80f, 1f);
            if (v >= 50_000)    return new Color(0.40f, 1f,    0.40f);
            return Color.white;
        }
    }
}
