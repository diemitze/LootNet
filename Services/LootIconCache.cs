using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using UnityEngine;

namespace LootNet.Services
{
    /// Template id -> item icon sprite. Icons come out of the game's bundles asynchronously,
    /// so callers Request early and Get later.
    ///
    /// Two things make a naive cache here fail until the game is restarted, and both are
    /// guarded below:
    ///   - GetItemSpriteAsync reaches through Singleton&lt;ItemIconCreator&gt;.Instance, which is
    ///     null during menu/raid transitions. That fault is transient, so failures are counted
    ///     and retried instead of blacklisted.
    ///   - The sprite comes from ResourcesCache, so it can be destroyed under us when its
    ///     bundle unloads. A cached entry is revalidated on every read.
    internal static class LootIconCache
    {
        private const int MaxAttempts = 3;

        private static readonly Dictionary<string, Sprite> _cache    = new();
        private static readonly HashSet<string>            _loading  = new();
        private static readonly Dictionary<string, int>    _attempts = new();

        public static Sprite Get(string templateId)
        {
            if (string.IsNullOrEmpty(templateId)) return null;
            if (!_cache.TryGetValue(templateId, out var sprite)) return null;

            if (sprite == null)              // destroyed with its bundle
            {
                _cache.Remove(templateId);
                _attempts.Remove(templateId);
                return null;
            }
            return sprite;
        }

        public static bool IsPending(string templateId)
            => templateId != null && _loading.Contains(templateId);

        public static void Request(string templateId)
        {
            if (string.IsNullOrEmpty(templateId)) return;
            if (Get(templateId) != null || _loading.Contains(templateId)) return;
            if (_attempts.TryGetValue(templateId, out int tries) && tries >= MaxAttempts) return;

            _loading.Add(templateId);
            Plugin.Instance.StartCoroutine(Load(templateId));
        }

        /// Call when the game state changes: anything that failed because the UI singleton was
        /// mid-transition deserves another go.
        public static void RetryFailed()
        {
            _attempts.Clear();
        }

        private static IEnumerator Load(string templateId)
        {
            try
            {
                var task = StartLoad(templateId);
                if (task == null) { RecordFailure(templateId); yield break; }

                while (!task.IsCompleted) yield return null;

                var sprite = ReadResult(task, templateId);
                if (sprite != null) { _cache[templateId] = sprite; _attempts.Remove(templateId); }
                else                  RecordFailure(templateId);
            }
            finally
            {
                _loading.Remove(templateId);   // must clear even if the coroutine is torn down
            }
        }

        private static Task<Sprite> StartLoad(string templateId)
        {
            try
            {
                var factory = Singleton<ItemFactory>.Instance;
                if (factory == null) return null;

                // A throwaway instance is the only way in: the sprite lookup takes an Item, not an id.
                var item = factory.CreateItem(MongoID.Generate(false), templateId, null);
                if (item == null) return null;

                return ItemViewFactory.GetItemSpriteAsync(item, 1);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[LootNet] Icon load failed for {templateId}: {ex.Message}");
                return null;
            }
        }

        private static Sprite ReadResult(Task<Sprite> task, string templateId)
        {
            try { return task.Result; }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[LootNet] Icon result failed for {templateId}: {ex.Message}");
                return null;
            }
        }

        private static void RecordFailure(string templateId)
        {
            _attempts.TryGetValue(templateId, out int tries);
            _attempts[templateId] = tries + 1;
        }
    }
}
