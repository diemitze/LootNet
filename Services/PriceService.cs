using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace LootNet.Services
{
    public class PriceService : MonoBehaviour
    {
        public Dictionary<string, double> Prices { get; private set; } = new();
        public bool IsLoaded { get; private set; } = false;

        public void FetchPrices()
        {
            _ = FetchPricesAsync();
        }

        private async Task FetchPricesAsync()
        {
            if (Plugin.UseHandbookPrices.Value)
            {
                Plugin.LogSource.LogInfo("LootNet: handbook prices enabled, skipping flea fetch");
                await FetchHandbookPrices();
                return;
            }

            if (await TryFetchFleaPrices()) return;
            Plugin.LogSource.LogWarning("LootNet: server mod not available, falling back to handbook prices");
            await FetchHandbookPrices();
        }

        private async Task<bool> TryFetchFleaPrices()
        {
            try
            {
                using HttpRequestMessage request = RequestHandler.HttpClient.CreateNewHttpRequest(
                    HttpMethod.Get, "/lootnet/prices");
                using HttpResponseMessage response = await RequestHandler.HttpClient.HttpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode) return false;

                byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                string json = DecodeBody(bytes);
                if (string.IsNullOrEmpty(json)) return false;

                JObject envelope = JObject.Parse(json);
                JToken data = envelope["data"];
                if (data == null) return false;

                string innerJson = data.Type == JTokenType.String
                    ? data.Value<string>() ?? string.Empty
                    : data.ToString();

                Prices = JsonConvert.DeserializeObject<Dictionary<string, double>>(innerJson) ?? new();
                IsLoaded = true;
                Plugin.LogSource.LogInfo($"LootNet: loaded {Prices.Count} flea prices");
                RaidTracker.RefreshPrices();
                return true;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"LootNet: flea price fetch failed - {ex.Message}");
                return false;
            }
        }

        private async Task FetchHandbookPrices()
        {
            try
            {
                using HttpRequestMessage request = RequestHandler.HttpClient.CreateNewHttpRequest(
                    HttpMethod.Get, "/client/handbook/templates");
                using HttpResponseMessage response = await RequestHandler.HttpClient.HttpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    Plugin.LogSource.LogError("LootNet: handbook fetch failed");
                    return;
                }

                byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                string json = DecodeBody(bytes);
                if (string.IsNullOrEmpty(json)) return;

                JArray items = JObject.Parse(json)?["data"]?["Items"] as JArray;
                if (items == null) return;

                foreach (JObject item in items)
                {
                    string id = item["Id"]?.Value<string>();
                    double price = item["Price"]?.Value<double>() ?? 0;
                    if (!string.IsNullOrEmpty(id) && price > 0)
                        Prices[id] = price;
                }

                IsLoaded = true;
                Plugin.LogSource.LogInfo($"LootNet: loaded {Prices.Count} handbook prices (fallback)");
                RaidTracker.RefreshPrices();
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"LootNet: handbook fallback failed - {ex.Message}");
            }
        }

        // SPT 4.1.3 streams the biggest routes (handbook/templates among them) instead of
        // building a string, so the body is no longer guaranteed to be zlib-wrapped. Sniff
        // the header and fall through to plain text rather than assuming either shape.
        private static string DecodeBody(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;

            try
            {
                if (IsZlib(bytes))
                {
                    using MemoryStream compressed = new(bytes, 2, bytes.Length - 2);
                    using DeflateStream deflate = new(compressed, CompressionMode.Decompress);
                    using StreamReader reader = new(deflate);
                    return reader.ReadToEnd();
                }

                if (bytes.Length > 2 && bytes[0] == 0x1F && bytes[1] == 0x8B)
                {
                    using MemoryStream compressed = new(bytes);
                    using GZipStream gzip = new(compressed, CompressionMode.Decompress);
                    using StreamReader reader = new(gzip);
                    return reader.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"LootNet: body decompress failed, trying raw - {ex.Message}");
            }

            return Encoding.UTF8.GetString(bytes).TrimStart('﻿');
        }

        private static bool IsZlib(byte[] bytes)
        {
            if (bytes.Length <= 2) return false;
            if ((bytes[0] & 0x0F) != 0x08) return false;      // deflate compression method
            return ((bytes[0] << 8) | bytes[1]) % 31 == 0;    // zlib header checksum
        }

        public double GetPrice(string templateId)
        {
            return Prices.TryGetValue(templateId, out double price) ? price : 0;
        }
    }
}
