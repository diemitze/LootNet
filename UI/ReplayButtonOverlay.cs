using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LootNet.UI
{

    public class ReplayButtonOverlay : MonoBehaviour
    {
        private static ReplayButtonOverlay _instance;
        public static ReplayButtonOverlay Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("LootNetReplayButton");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<ReplayButtonOverlay>();
                }
                return _instance;
            }
        }

        private static readonly Color Gold   = new(1f, 0.84f, 0f);
        private static readonly Color Idle   = new(0.78f, 0.78f, 0.78f);
        private static readonly Color BgIdle = new(0.05f, 0.05f, 0.07f, 0.92f);
        private static readonly Color BgHot  = new(0.10f, 0.09f, 0.06f, 0.95f);

        private GameObject      _root;
        private CanvasGroup     _cg;
        private Image           _bg;
        private Image           _stripe;
        private TextMeshProUGUI _label;

        private GameObject _watched;
        private Action     _onClick;

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            BuildUI();
        }

        private void Update()
        {
            if (_root == null || !_root.activeSelf) return;
            // the end screen went away (next raid, menu, etc)
            if (_watched == null || !_watched.activeInHierarchy) HideNow();
        }

        public void ShowFor(GameObject endScreen, Action onClick)
        {
            _watched = endScreen;
            _onClick = onClick;
            _root.SetActive(true);
            StopAllCoroutines();
            StartCoroutine(FadeIn(0.35f));
        }

        public void HideNow()
        {
            StopAllCoroutines();
            if (_cg != null) _cg.alpha = 0f;
            if (_root != null) _root.SetActive(false);
        }

        private IEnumerator FadeIn(float dur)
        {
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                _cg.alpha = Mathf.Clamp01(t / dur);
                yield return null;
            }
            _cg.alpha = 1f;
        }

        private void BuildUI()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 480;   // under the summary itself (500)

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight  = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            _root = new GameObject("Button");
            _root.transform.SetParent(transform, false);
            var rt = _root.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.zero;
            rt.pivot     = Vector2.zero;
            rt.anchoredPosition = new Vector2(60f, 60f);
            rt.sizeDelta = new Vector2(250f, 48f);

            _cg = _root.AddComponent<CanvasGroup>();
            _cg.alpha = 0f;

            _bg = _root.AddComponent<Image>();
            _bg.color = BgIdle;

            var stripeGo = new GameObject("Stripe");
            stripeGo.transform.SetParent(_root.transform, false);
            var srt = stripeGo.AddComponent<RectTransform>();
            srt.anchorMin = Vector2.zero; srt.anchorMax = new Vector2(0f, 1f);
            srt.pivot = new Vector2(0f, 0.5f);
            srt.anchoredPosition = Vector2.zero;
            srt.sizeDelta = new Vector2(3f, 0f);
            _stripe = stripeGo.AddComponent<Image>();
            _stripe.color = Gold;

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(_root.transform, false);
            var lrt = labelGo.AddComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(18f, 0f); lrt.offsetMax = new Vector2(-14f, 0f);
            _label = labelGo.AddComponent<TextMeshProUGUI>();
            _label.text             = "REPLAY SUMMARY";
            _label.fontSize         = 15f;
            _label.fontStyle        = FontStyles.Bold;
            _label.color            = Idle;
            _label.alignment        = TextAlignmentOptions.MidlineLeft;
            _label.characterSpacing = 4f;
            _label.raycastTarget    = false;

            var btn = _root.AddComponent<Button>();
            btn.targetGraphic = _bg;
            btn.transition    = Selectable.Transition.None;
            btn.onClick.AddListener(() => _onClick?.Invoke());

            var et = _root.AddComponent<EventTrigger>();
            AddTrigger(et, EventTriggerType.PointerEnter, _ => { _label.color = Gold; _bg.color = BgHot;  });
            AddTrigger(et, EventTriggerType.PointerExit,  _ => { _label.color = Idle; _bg.color = BgIdle; });

            _root.SetActive(false);
        }

        private static void AddTrigger(EventTrigger et, EventTriggerType type,
            UnityEngine.Events.UnityAction<BaseEventData> action)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(action);
            et.triggers.Add(entry);
        }
    }
}
