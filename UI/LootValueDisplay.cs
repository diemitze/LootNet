using TMPro;
using UnityEngine;

namespace LootNet.UI
{
    /// <summary>
    /// Clones EFT's own SlotViewHeader element so the display looks 100% native.
    /// Identical approach to the original LootNet mod.
    /// </summary>
    public class LootValueDisplay : MonoBehaviour
    {
        private static LootValueDisplay _instance;
        public static LootValueDisplay Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("LootNetDisplay");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<LootValueDisplay>();
                }
                return _instance;
            }
        }

        private GameObject       _original;     // the EFT SlotViewHeader we clone from
        private GameObject       _clone;        // our cloned instance living in the inventory UI
        private TextMeshProUGUI  _cloneLabel;   // cached TMP child ref to avoid Find() each refresh
        private double           _currentValue;

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
        }

        // ── Called every time the inventory opens ─────────────────────────────────

        public void Show()
        {
            EnsureClone();
            if (_clone != null)
            {
                _clone.SetActive(true);
                RefreshText();
            }
        }

        public void Hide()
        {
            if (_clone != null)
                _clone.SetActive(false);
        }

        public void SetValue(double value)
        {
            _currentValue = value;
            RefreshText();
        }

        public void DestroyClone()
        {
            if (_clone != null)
            {
                Destroy(_clone);
                _clone = null;
            }
            _cloneLabel   = null;
            _original     = null;
            _currentValue = 0;
        }

        public void TryCreateFromNativeUI() { }   // kept for compatibility

        // ── Internal ──────────────────────────────────────────────────────────────

        private void EnsureClone()
        {
            // Already have a live clone
            if (_clone != null) return;

            // Only create the clone when actually in a raid
            if (!Comfort.Common.Singleton<EFT.GameWorld>.Instantiated) return;

            // Find the native SlotViewHeader the first time (or if it was lost)
            if (_original == null)
            {
                _original = GameObject.Find(
                    "Common UI/Common UI/InventoryScreen/Items Panel/LeftSide/" +
                    "Containers Panel/Scrollview Parent/Containers Scrollview/" +
                    "Content/TacticalVest Slot/Header Panel/SlotViewHeader");
            }

            if (_original == null)
            {
                Plugin.LogSource.LogWarning("LootNet: SlotViewHeader not found — inventory path may differ");
                return;
            }

            // Clone it and place next to the original (same parent)
            _clone = Instantiate(_original, _original.transform.parent, false);

            var rt = _clone.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchoredPosition3D += new Vector3(0, 36, 0);
                rt.localScale = new Vector3(1.75f, 1.75f, 1);
            }

            // Hide the expand/collapse arrow
            var arrow = _clone.transform.Find("ArrowHolder");
            if (arrow != null) arrow.gameObject.SetActive(false);

            // Cache the TMP child once so RefreshText doesn't Find() every call
            _cloneLabel = _clone.transform.Find("SlotName")?.GetComponent<TextMeshProUGUI>();

            RefreshText();
        }

        private void RefreshText()
        {
            if (_cloneLabel == null) return;
            _cloneLabel.text                = $"₽ {_currentValue:N0}";
            _cloneLabel.horizontalAlignment = TMPro.HorizontalAlignmentOptions.Left;
            _cloneLabel.color               = GetColor(_currentValue);
        }

        private static Color GetColor(double v)
        {
            if (v >= 1_000_000) return new Color(1f, 0.2f, 0.2f);
            if (v >= 500_000)   return new Color(1f, 0f,   1f);
            if (v >= 300_000)   return new Color(1f, 0.4f, 0.8f);
            if (v >= 150_000)   return new Color(0.4f, 0.8f, 1f);
            if (v >= 50_000)    return new Color(0.4f, 1f,  0.4f);
            return new Color(0.85f, 0.85f, 0.85f);
        }
    }
}
