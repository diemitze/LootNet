#nullable enable
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;
using System.Collections.Generic;
using System.Text.Json;

namespace LootNetServer;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.20fpsguy.LootNetServer";
    public override string Name { get; init; } = "LootNetServer";
    public override string Author { get; init; } = "20fpsguy";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new("1.0.0");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.0");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; } = "";
    public override bool? IsBundleMod { get; init; }
    public override string License { get; init; } = "MIT";
}

[Injectable]
public class LootNetRouter(JsonUtil jsonUtil, LootNetCallback callback) : StaticRouter(jsonUtil, [
    new RouteAction<EmptyRequestData>(
        "/lootnet/prices",
        async (url, info, sessionId, output) => await callback.HandleGetPrices(url, info, sessionId)
    )
])
{ }

[Injectable]
public class LootNetCallback(
    HttpResponseUtil httpResponseUtil,
    RagfairPriceService ragfairPriceService)
{
    public ValueTask<string> HandleGetPrices(string url, EmptyRequestData info, MongoId sessionId)
    {
        var prices = ragfairPriceService.GetAllFleaPrices();
        var stringPrices = new Dictionary<string, double>();
        foreach (var kvp in prices)
            stringPrices[kvp.Key.ToString()] = kvp.Value;
        return new ValueTask<string>(httpResponseUtil.GetBody(JsonSerializer.Serialize(stringPrices)));
    }
}
