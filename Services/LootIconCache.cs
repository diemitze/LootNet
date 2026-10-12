using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using UnityEngine;

namespace LootNet.Services
{
    /// Loot entry -> item icon, through the same path the stash uses (LoadItemIcon). That path
    /// queues renders one at a time and reuses the game's on-disk icon cache.
    /// GetItemSpriteAsync skips the render lock, so a burst of requests captured over each
    /// other and came back blank or wrong; don't go back to it.
    internal static class LootIconCache
    {
        // A render that has not delivered by then is assumed lost (sprite destroyed with its
        // bundle, creator torn down mid-transition) and gets asked for again.
        private const float RetryAfterSeconds = 10f;

        private static readonly Dictionary<string, (ItemIcon Icon, float At)> _icons = new();

        /// With a live item the icon is rendered from it, so a gun shows its attachments.
        /// Without one (replays of saved history) it comes from the bare template.
        public static string KeyFor(LootEntry e)
            => e.LiveItem != null ? "item:" + e.LiveItem.Id : e.TemplateId;

        public static Sprite Get(string key)
        {
            if (key == null || !_icons.TryGetValue(key, out var entry)) return null;
            var sprite = entry.Icon?.Sprite;
            return sprite != null ? sprite : null;   // Unity null when destroyed
        }

        public static bool IsPending(string key)
            => key != null && _icons.TryGetValue(key, out var entry)
               && Get(key) == null && Time.realtimeSinceStartup - entry.At < RetryAfterSeconds;

        public static void Request(LootEntry e)
        {
            if (e == null) return;
            string key = KeyFor(e);
            if (string.IsNullOrEmpty(key) || Get(key) != null || IsPending(key)) return;

            try
            {
                var item = e.LiveItem ?? CreateFromTemplate(e.TemplateId);
                if (item == null) return;
                var icon = ItemViewFactory.LoadItemIcon(item, 1, false);
                _icons[key] = (icon, Time.realtimeSinceStartup);
            }
            catch (Exception ex)
            {
                // ItemIconCreator is null during menu/raid transitions; the next Request retries.
                Plugin.LogSource.LogWarning($"[LootNet] Icon request failed for {e.ShortName ?? key}: {ex.Message}");
            }
        }

        private static Item CreateFromTemplate(string templateId)
        {
            if (string.IsNullOrEmpty(templateId)) return null;
            var factory = Singleton<ItemFactory>.Instance;
            return factory?.CreateItem(MongoID.Generate(false), templateId, null);
        }
    }
}
