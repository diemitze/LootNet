using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
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
                if (bytes.Length <= 2) return false;

                using MemoryStream compressedStream = new(bytes, 2, bytes.Length - 2);
                using DeflateStream deflateStream = new(compressedStream, CompressionMode.Decompress);
                using StreamReader reader = new(deflateStream);
                string json = await reader.ReadToEndAsync();

                JObject envelope = JObject.Parse(json);
                JToken data = envelope["data"];
                if (data == null) return false;

                string innerJson = data.Type == JTokenType.String
                    ? data.Value<string>() ?? string.Empty
                    : data.ToString();

                Prices = JsonConvert.DeserializeObject<Dictionary<string, double>>(innerJson) ?? new();
                IsLoaded = true;
                Plugin.LogSource.LogInfo($"LootNet: loaded {Prices.Count} flea prices");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"LootNet: flea price fetch failed — {ex.Message}");
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
                if (bytes.Length <= 2) return;

                using MemoryStream compressedStream = new(bytes, 2, bytes.Length - 2);
                using DeflateStream deflateStream = new(compressedStream, CompressionMode.Decompress);
                using StreamReader reader = new(deflateStream);
                string json = await reader.ReadToEndAsync();

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
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"LootNet: handbook fallback failed — {ex.Message}");
            }
        }

        public double GetPrice(string templateId)
        {
            return Prices.TryGetValue(templateId, out double price) ? price : 0;
        }
    }
}
