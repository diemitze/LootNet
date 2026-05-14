using EFT.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LootNet.UI
{
    internal static class RaidHistoryMenuButton
    {
        private static GameObject _button;

        public static void TryInject()
        {
            if (_button) return;

            var taskBar = UnityEngine.Object.FindObjectOfType<MenuTaskBar>(true);
            if (taskBar == null)
            {
                Plugin.LogSource.LogWarning("[LootNet] MenuTaskBar not found in scene");
                return;
            }

            // The nav buttons (CHARACTER, TRADERS…) live in a HorizontalLayoutGroup.
            // Find whichever one has the most children - that's the main nav container.
            var container = FindNavContainer(taskBar);
            if (container == null)
            {
                Plugin.LogSource.LogWarning("[LootNet] Could not find nav HorizontalLayoutGroup in MenuTaskBar");
                return;
            }

            _button = BuildNavButton(container);
        }

        // ── Finding the right container ──────────────────────────────────────────

        private static Transform FindNavContainer(MenuTaskBar taskBar)
        {
            HorizontalLayoutGroup best = null;
            int bestCount = 0;
            foreach (var hlg in taskBar.GetComponentsInChildren<HorizontalLayoutGroup>(true))
            {
                if (hlg.transform.childCount > bestCount)
                {
                    bestCount = hlg.transform.childCount;
                    best = hlg;
                }
            }
            return best?.transform;
        }

        // ── Building the button ──────────────────────────────────────────────────

        private static GameObject BuildNavButton(Transform parent)
        {
            var siblingLabel = parent.GetComponentInChildren<TextMeshProUGUI>(true);
            float fontSize    = siblingLabel != null ? siblingLabel.fontSize    : 12f;
            Color normalColor = siblingLabel != null ? siblingLabel.color       : new Color(0.85f, 0.85f, 0.85f);
            float charSpacing = siblingLabel != null ? siblingLabel.characterSpacing : 2f;

            var go = new GameObject("LootNetHistoryButton");
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 130f;
            le.flexibleWidth  = 0f;

            var bg = go.AddComponent<Image>();
            bg.color = Color.clear;

            // 3-bar list icon anchored left-center.
            const float IconW = 14f;
            const float IconX = 8f;
            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(go.transform, false);
            var iconRt = iconGo.AddComponent<RectTransform>();
            iconRt.anchorMin        = new Vector2(0f, 0.5f);
            iconRt.anchorMax        = new Vector2(0f, 0.5f);
            iconRt.pivot            = new Vector2(0f, 0.5f);
            iconRt.anchoredPosition = new Vector2(IconX, 0f);
            iconRt.sizeDelta        = new Vector2(IconW, 11f);

            var bars = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                var barGo = new GameObject($"Bar{i}");
                barGo.transform.SetParent(iconGo.transform, false);
                var barRt = barGo.AddComponent<RectTransform>();
                // Stack three 2px-tall bars with 2.5px gap.
                barRt.anchorMin        = new Vector2(0f, 1f);
                barRt.anchorMax        = new Vector2(1f, 1f);
                barRt.pivot            = new Vector2(0f, 1f);
                barRt.anchoredPosition = new Vector2(0f, -i * 4.5f);
                barRt.sizeDelta        = new Vector2(0f, 2f);
                bars[i] = barGo.AddComponent<Image>();
                bars[i].color = normalColor;
            }

            // Label offset to the right of the icon.
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            var labelRt = labelGo.AddComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(IconX + IconW + 4f, 0f);
            labelRt.offsetMax = new Vector2(-4f, 0f);
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text             = "HISTORY";
            label.fontSize         = fontSize;
            label.fontStyle        = FontStyles.Bold;
            label.color            = normalColor;
            label.alignment        = TextAlignmentOptions.MidlineLeft;
            label.characterSpacing = charSpacing;
            label.overflowMode     = TextOverflowModes.Ellipsis;
            if (siblingLabel?.font != null) label.font = siblingLabel.font;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.transition    = Selectable.Transition.None;
            btn.onClick.AddListener(OpenHistory);

            Color gold = new Color(1f, 0.84f, 0f);
            Color dim  = new Color(normalColor.r * 0.6f, normalColor.g * 0.6f, normalColor.b * 0.6f);
            var et = go.AddComponent<EventTrigger>();
            AddTrigger(et, EventTriggerType.PointerEnter, _ => { label.color = gold; SetBars(bars, gold); });
            AddTrigger(et, EventTriggerType.PointerExit,  _ => { label.color = normalColor; SetBars(bars, normalColor); });
            AddTrigger(et, EventTriggerType.PointerDown,  _ => { label.color = dim; SetBars(bars, dim); });
            AddTrigger(et, EventTriggerType.PointerUp,    _ => { label.color = gold; SetBars(bars, gold); });

            go.SetActive(true);
            return go;
        }

        private static void SetBars(Image[] bars, Color c)
        {
            foreach (var b in bars) b.color = c;
        }

        private static void AddTrigger(EventTrigger et, EventTriggerType type,
            UnityEngine.Events.UnityAction<BaseEventData> action)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(action);
            et.triggers.Add(entry);
        }

        private static void OpenHistory() => RaidHistoryDisplay.Instance.Show();
    }
}
