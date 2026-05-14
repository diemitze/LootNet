using LootNet.Services;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LootNet.UI
{
    public class RaidHistoryDisplay : MonoBehaviour
    {
        private static RaidHistoryDisplay _instance;
        public static RaidHistoryDisplay Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("LootNetHistory");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<RaidHistoryDisplay>();
                }
                return _instance;
            }
        }

        private CanvasGroup       _canvasGroup;
        private GameObject        _root;
        private RectTransform     _drawerRt;
        private Transform         _cardContainer;
        private RectTransform     _contentRt;
        private ScrollRect        _scrollRect;
        private TextMeshProUGUI   _emptyLabel;
        private TextMeshProUGUI   _capLabel;
        private bool              _visible;

        private const float DrawerW     = 420f;
        private const float HeaderH     = 80f;
        private const float CardH       = 96f;
        private const float CardSpacing = 4f;

        private static readonly Color Gold = new Color(0.38f, 0.70f, 1.00f);

        private readonly List<GameObject> _cards = new();

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            BuildUI();
        }

        // ── Show / Hide ──────────────────────────────────────────────────────────

        public void Show()
        {
            if (_visible) return;
            _root.SetActive(true);
            RebuildCards();
            if (_scrollRect != null) _scrollRect.verticalNormalizedPosition = 1f;
            _visible = true;
            StopAllCoroutines();
            StartCoroutine(SlideIn(0.22f));
        }

        public void Hide()
        {
            if (!_visible) return;
            _visible = false;
            StopAllCoroutines();
            StartCoroutine(SlideOut(0.18f));
        }

        private IEnumerator SlideIn(float dur)
        {
            _canvasGroup.alpha = 0f;
            _drawerRt.anchoredPosition = new Vector2(DrawerW, 0f);
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float p = Ease(t / dur);
                _drawerRt.anchoredPosition = new Vector2(Mathf.Lerp(DrawerW, 0f, p), 0f);
                _canvasGroup.alpha         = Mathf.Clamp01(t / dur * 2f);
                yield return null;
            }
            _drawerRt.anchoredPosition = Vector2.zero;
            _canvasGroup.alpha = 1f;
        }

        private IEnumerator SlideOut(float dur)
        {
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float p = Ease(t / dur);
                _drawerRt.anchoredPosition = new Vector2(Mathf.Lerp(0f, DrawerW, p), 0f);
                _canvasGroup.alpha         = Mathf.Lerp(1f, 0f, t / dur);
                yield return null;
            }
            _canvasGroup.alpha = 0f;
            _root.SetActive(false);
        }

        private static float Ease(float t) => t < 0.5f ? 2f * t * t : 1f - 2f * (1f - t) * (1f - t);

        // ── Cards ────────────────────────────────────────────────────────────────

        private void RebuildCards()
        {
            foreach (var c in _cards) Destroy(c);
            _cards.Clear();

            var history = RaidTracker.RaidHistory;
            for (int i = history.Count - 1; i >= 0; i--)
                _cards.Add(BuildCard(history[i], _cards.Count));

            _contentRt.sizeDelta = new Vector2(0f, _cards.Count * (CardH + CardSpacing));

            if (_emptyLabel != null)
                _emptyLabel.gameObject.SetActive(_cards.Count == 0);
            if (_capLabel != null)
                _capLabel.gameObject.SetActive(_cards.Count >= RaidTracker.MaxHistory);
        }

        private GameObject BuildCard(RaidStats stats, int index)
        {
            var card = MakeRect($"Card{index}", _cardContainer);
            var rt   = card.GetComponent<RectTransform>();
            rt.anchorMin        = new Vector2(0f, 1f);
            rt.anchorMax        = new Vector2(1f, 1f);
            rt.pivot            = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -index * (CardH + CardSpacing));
            rt.sizeDelta        = new Vector2(0f, CardH);

            // Card background
            var bg = card.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.08f, 0.11f, 1f);

            // Bottom separator
            var sep   = MakeRect("Sep", card.transform);
            var sepRt = sep.GetComponent<RectTransform>();
            sepRt.anchorMin = new Vector2(0.03f, 0f);
            sepRt.anchorMax = new Vector2(0.97f, 0f);
            sepRt.pivot     = new Vector2(0.5f, 0f);
            sepRt.sizeDelta = new Vector2(0f, 1f);
            sep.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.05f);

            // Left accent bar — coloured by value tier
            Color accentCol = ValueColor(stats.TotalFoundValue);
            var accent   = MakeRect("Accent", card.transform);
            var accentRt = accent.GetComponent<RectTransform>();
            accentRt.anchorMin = new Vector2(0f, 0.1f);
            accentRt.anchorMax = new Vector2(0f, 0.9f);
            accentRt.pivot     = new Vector2(0f, 0.5f);
            accentRt.sizeDelta = new Vector2(3f, 0f);
            accent.AddComponent<Image>().color = accentCol;

            const float PadL = 16f;
            const float PadR = 14f;

            // ── Left column ──────────────────────────────────────────────────────

            // Map name
            var mapLbl = MakeTMP("Map", card.transform, 17f, FontStyles.Bold, TextAlignmentOptions.Left);
            mapLbl.text  = stats.MapName.ToUpper();
            mapLbl.color = Color.white;
            SetRect(mapLbl.rectTransform,
                anchorMin: new Vector2(0f, 0.46f), anchorMax: new Vector2(0.58f, 1f),
                offsetMin: new Vector2(PadL, 0f),  offsetMax: new Vector2(0f, -8f));

            // Type badge + relative time
            string raidType = stats.IsScavRaid ? "SCAV" : "PMC";
            Color  typeCol  = stats.IsScavRaid
                ? new Color(0.80f, 0.62f, 0.30f)
                : new Color(0.35f, 0.65f, 0.92f);
            var subLbl = MakeTMP("Sub", card.transform, 10f, FontStyles.Bold, TextAlignmentOptions.Left);
            subLbl.richText = true;
            subLbl.text     = $"<color=#{ColorUtility.ToHtmlStringRGB(typeCol)}>{raidType}</color>" +
                              $"<color=#555555>  ·  {RelativeTime(stats.RaidTime)}</color>";
            SetRect(subLbl.rectTransform,
                anchorMin: new Vector2(0f, 0f),    anchorMax: new Vector2(0.58f, 0.48f),
                offsetMin: new Vector2(PadL, 6f),  offsetMax: new Vector2(0f, 0f));

            // ── Right column ─────────────────────────────────────────────────────

            // Loot value
            var valLbl = MakeTMP("Value", card.transform, 18f, FontStyles.Bold, TextAlignmentOptions.Right);
            valLbl.text  = $"₽ {stats.TotalFoundValue:N0}";
            valLbl.color = accentCol;
            SetRect(valLbl.rectTransform,
                anchorMin: new Vector2(0.42f, 0.46f), anchorMax: new Vector2(1f, 1f),
                offsetMin: new Vector2(0f, 0f),       offsetMax: new Vector2(-PadR, -8f));

            // Kill summary
            var killLbl = MakeTMP("Kills", card.transform, 10f, FontStyles.Normal, TextAlignmentOptions.Right);
            killLbl.text  = BuildKillString(stats);
            killLbl.color = new Color(0.45f, 0.45f, 0.45f);
            SetRect(killLbl.rectTransform,
                anchorMin: new Vector2(0.42f, 0f), anchorMax: new Vector2(1f, 0.48f),
                offsetMin: new Vector2(0f, 6f),    offsetMax: new Vector2(-PadR, 0f));

            return card;
        }

        private static string BuildKillString(RaidStats s)
        {
            var parts = new List<string>();
            if (s.PmcKills  > 0) parts.Add($"{s.PmcKills} PMC");
            if (s.ScavKills > 0) parts.Add($"{s.ScavKills} Scav");
            if (s.FireteamMembers != null && s.FireteamMembers.Count > 0)
            {
                int squadKills = 0;
                foreach (var m in s.FireteamMembers) squadKills += m.Kills;
                if (squadKills > 0) parts.Add($"Squad: {squadKills}");
            }
            return parts.Count > 0 ? string.Join("  ·  ", parts) : "No kills";
        }

        // ── UI construction ──────────────────────────────────────────────────────

        private void BuildUI()
        {
            var canvas = gameObject.GetComponent<Canvas>() ?? gameObject.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 490;
            if (!gameObject.GetComponent<CanvasScaler>())     gameObject.AddComponent<CanvasScaler>();
            if (!gameObject.GetComponent<GraphicRaycaster>()) gameObject.AddComponent<GraphicRaycaster>();
            _canvasGroup = gameObject.GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

            _root = MakeRect("HistoryRoot", transform);
            Stretch(_root.GetComponent<RectTransform>());

            // Semi-transparent overlay — click to close
            var overlay = MakeRect("Overlay", _root.transform);
            Stretch(overlay.GetComponent<RectTransform>());
            overlay.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            var overlayBtn = overlay.AddComponent<Button>();
            overlayBtn.transition = Selectable.Transition.None;
            overlayBtn.onClick.AddListener(Hide);

            // Drawer — anchored to right edge, slides in/out on X
            var drawerGo = MakeRect("Drawer", _root.transform);
            _drawerRt            = drawerGo.GetComponent<RectTransform>();
            _drawerRt.anchorMin  = new Vector2(1f, 0f);
            _drawerRt.anchorMax  = new Vector2(1f, 1f);
            _drawerRt.pivot      = new Vector2(1f, 0.5f);
            _drawerRt.sizeDelta  = new Vector2(DrawerW, 0f);
            _drawerRt.anchoredPosition = new Vector2(DrawerW, 0f);

            // Drawer background — block raycasts so overlay click-close doesn't fire through
            var drawerBg = drawerGo.AddComponent<Image>();
            drawerBg.color = new Color(0.06f, 0.06f, 0.09f, 1f);

            // Gold left border
            var border   = MakeRect("Border", drawerGo.transform);
            var borderRt = border.GetComponent<RectTransform>();
            borderRt.anchorMin = Vector2.zero;
            borderRt.anchorMax = new Vector2(0f, 1f);
            borderRt.pivot     = new Vector2(0f, 0.5f);
            borderRt.sizeDelta = new Vector2(3f, 0f);
            border.AddComponent<Image>().color = Gold;

            // Gold top bar
            var topBar   = MakeRect("TopBar", drawerGo.transform);
            var topBarRt = topBar.GetComponent<RectTransform>();
            topBarRt.anchorMin = new Vector2(0f, 1f);
            topBarRt.anchorMax = new Vector2(1f, 1f);
            topBarRt.pivot     = Vector2.up;
            topBarRt.sizeDelta = new Vector2(0f, 3f);
            topBar.AddComponent<Image>().color = Gold;

            // Header title
            var title = MakeTMP("Title", drawerGo.transform, 22f, FontStyles.Bold, TextAlignmentOptions.Left);
            title.text             = "RAID HISTORY";
            title.color            = Gold;
            title.characterSpacing = 5f;
            SetRect(title.rectTransform,
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f),
                offsetMin: new Vector2(20f, -HeaderH + 12f), offsetMax: new Vector2(-16f, -12f));

            // Subtitle
            var sub = MakeTMP("Sub", drawerGo.transform, 10f, FontStyles.Normal, TextAlignmentOptions.Left);
            sub.text             = "THIS SESSION  ·  CLICK OUTSIDE TO CLOSE";
            sub.color            = new Color(0.30f, 0.30f, 0.30f);
            sub.characterSpacing = 2f;
            SetRect(sub.rectTransform,
                anchorMin: new Vector2(0f, 1f), anchorMax: new Vector2(1f, 1f),
                offsetMin: new Vector2(20f, -HeaderH + 2f), offsetMax: new Vector2(-16f, -HeaderH + 18f));

            // Divider under header
            var div   = MakeRect("Divider", drawerGo.transform);
            var divRt = div.GetComponent<RectTransform>();
            divRt.anchorMin        = new Vector2(0f, 1f);
            divRt.anchorMax        = new Vector2(1f, 1f);
            divRt.pivot            = Vector2.up;
            divRt.anchoredPosition = new Vector2(0f, -HeaderH);
            divRt.sizeDelta        = new Vector2(0f, 1f);
            div.AddComponent<Image>().color = new Color(0.38f, 0.70f, 1.00f, 0.20f);

            // Scroll area
            var scrollGo = MakeRect("Scroll", drawerGo.transform);
            var scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = new Vector2(0f, 0f);
            scrollRt.offsetMax = new Vector2(0f, -(HeaderH + 1f));
            _scrollRect = scrollGo.AddComponent<ScrollRect>();
            _scrollRect.horizontal = false;
            _scrollRect.scrollSensitivity = 30f;

            var viewportGo = MakeRect("Viewport", scrollGo.transform);
            Stretch(viewportGo.GetComponent<RectTransform>());
            viewportGo.AddComponent<RectMask2D>();
            _scrollRect.viewport = viewportGo.GetComponent<RectTransform>();

            var contentGo = MakeRect("Content", viewportGo.transform);
            _contentRt            = contentGo.GetComponent<RectTransform>();
            _contentRt.anchorMin  = new Vector2(0f, 1f);
            _contentRt.anchorMax  = new Vector2(1f, 1f);
            _contentRt.pivot      = new Vector2(0.5f, 1f);
            _contentRt.anchoredPosition = Vector2.zero;
            _contentRt.sizeDelta        = Vector2.zero;
            _scrollRect.content   = _contentRt;
            _cardContainer        = contentGo.transform;

            // Empty-state message shown when no raids have been played yet
            _emptyLabel = MakeTMP("EmptyLabel", drawerGo.transform, 13f, FontStyles.Normal, TextAlignmentOptions.Center);
            _emptyLabel.text  = "No raids recorded yet.\nComplete a raid to see your history.";
            _emptyLabel.color = new Color(0.35f, 0.35f, 0.35f);
            _emptyLabel.lineSpacing = 8f;
            SetRect(_emptyLabel.rectTransform,
                anchorMin: new Vector2(0f, 0.3f), anchorMax: new Vector2(1f, 0.7f),
                offsetMin: new Vector2(24f, 0f),  offsetMax: new Vector2(-24f, 0f));
            _emptyLabel.gameObject.SetActive(false);

            // Cap notice shown at the bottom of the list when history is full
            _capLabel = MakeTMP("CapLabel", drawerGo.transform, 10f, FontStyles.Normal, TextAlignmentOptions.Center);
            _capLabel.text  = $"Showing last {RaidTracker.MaxHistory} raids  ·  oldest entries are dropped";
            _capLabel.color = new Color(0.28f, 0.28f, 0.28f);
            SetRect(_capLabel.rectTransform,
                anchorMin: new Vector2(0f, 0f), anchorMax: new Vector2(1f, 0f),
                offsetMin: new Vector2(16f, 8f), offsetMax: new Vector2(-16f, 28f));
            _capLabel.gameObject.SetActive(false);

            _canvasGroup.alpha = 0f;
            _root.SetActive(false);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static void SetRect(RectTransform rt,
            Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
        }

        private static GameObject MakeRect(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            return go;
        }

        private static TextMeshProUGUI MakeTMP(string name, Transform parent,
            float size, FontStyles style, TextAlignmentOptions align)
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

        private static string RelativeTime(DateTime t)
        {
            var s = DateTime.Now - t;
            if (s.TotalMinutes < 1)  return "Just now";
            if (s.TotalMinutes < 60) return $"{(int)s.TotalMinutes} min ago";
            if (s.TotalHours   < 24) return $"{(int)s.TotalHours}h {s.Minutes}m ago";
            return "Earlier today";
        }

        private static Color ValueColor(double v)
        {
            if (v >= 1_000_000) return new Color(1f,    0.20f, 0.20f);
            if (v >= 500_000)   return new Color(1f,    0f,    1f);
            if (v >= 300_000)   return new Color(1f,    0.40f, 0.80f);
            if (v >= 150_000)   return new Color(0.40f, 0.80f, 1f);
            if (v >= 50_000)    return new Color(0.40f, 1f,    0.40f);
            return new Color(0.38f, 0.70f, 1.00f);
        }
    }
}
