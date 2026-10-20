namespace Evenmere.Content.Tests.Support;

/// <summary>
/// The minimal pack plus one example of every optional field, effect kind and condition kind, so tests can check that
/// the loader reads the whole vocabulary. It lives in a temporary directory because the committed fixture stays minimal.
/// </summary>
internal static class KitchenSink
{
    /// <summary>Creates a temporary content root holding the minimal pack and the extra items.</summary>
    public static TempContent Create()
    {
        var content = TempContent.FromMinimal();

        content.Write("goods/bread.json", """{ "id": "bread", "category": "consumer", "era": 1 }""");
        content.Write("goods/coal.json", """{ "id": "coal", "category": "raw", "era": 1, "tags": ["fuel"] }""");
        content.Write("goods/grain.json", """{ "id": "grain", "category": "raw", "era": 1 }""");
        content.Write("goods/stone.json", """{ "id": "stone", "category": "construction", "era": 1 }""");

        content.Write("terrains/hills.json", """{ "id": "hills", "buildable": true }""");
        content.Write("terrains/marsh.json", """{ "id": "marsh", "buildable": false, "access": "marsh_drainage" }""");
        content.Write("terrains/highlands.json", """{ "id": "highlands", "buildable": true, "blocksRoads": false, "access": "highlands" }""");

        content.Write("greatworks/writing.json", """
            {
              "id": "writing", "source": "creativity", "baseCreativity": 100, "growthPercent": 25,
              "cultureByEra": [2, 4, 6, 8], "attractiveness": 1
            }
            """);
        content.Write("greatworks/relic.json", """{ "id": "relic", "source": "discovery", "cultureByEra": [2, 4, 6, 8] }""");

        content.Write("recipes/bake_bread.json", """
            { "id": "bake_bread", "inputs": { "grain": 1.25 }, "outputs": { "bread": 1 }, "cycleSeconds": 30 }
            """);

        content.Write("buildings/bakery.json", """
            {
              "id": "bakery", "category": "production", "era": 2, "footprint": [2, 2],
              "cost": { "timber": 6, "coin": 10 }, "buildDays": 2,
              "workforce": { "tier": "villagers", "count": 4 }, "recipe": "bake_bread",
              "upkeepCoin": 2, "tags": ["processing", "food"]
            }
            """);
        content.Write("buildings/grain_farm.json", """
            {
              "id": "grain_farm", "category": "production", "era": 1, "footprint": [5, 5],
              "placement": {
                "terrain": ["plains"],
                "fertility": { "crop": "grain", "minPercent": 25 },
                "areaYield": { "kind": "footprint_fertility" }
              },
              "cost": { "timber": 4 }, "buildDays": 2, "tags": ["farm", "seasonal"]
            }
            """);
        content.Write("buildings/quarry.json", """
            {
              "id": "quarry", "category": "production", "era": 1, "footprint": [3, 3],
              "placement": { "terrain": ["hills"], "deposit": "stone", "areaYield": { "kind": "footprint_deposit" } },
              "cost": { "timber": 6 }, "buildDays": 3
            }
            """);
        content.Write("buildings/paper_mill.json", """
            {
              "id": "paper_mill", "category": "production", "era": 2, "footprint": [3, 3],
              "placement": { "riverside": true, "roadAccess": true }, "buildDays": 3
            }
            """);
        content.Write("buildings/scribes_hall.json", """
            {
              "id": "scribes_hall", "category": "knowledge", "era": 1, "footprint": [3, 3], "buildDays": 4,
              "cost": { "timber": 8, "stone": 6 },
              "outputs": { "knowledge": 4 },
              "slots": [ { "type": "any", "count": 2 }, { "type": "writing", "count": 1 } ]
            }
            """);
        content.Write("buildings/storytellers_circle.json", """
            {
              "id": "storytellers_circle", "category": "culture", "era": 1, "footprint": [3, 3], "buildDays": 3,
              "outputs": { "culture": 3, "creativity": { "writing": 1 } }
            }
            """);
        content.Write("buildings/gasworks.json", """
            {
              "id": "gasworks", "category": "service", "era": 2, "footprint": [3, 3], "buildDays": 4,
              "service": { "kind": "gas", "coverage": 24 }, "fuel": { "good": "coal", "perDay": 1 }
            }
            """);
        content.Write("buildings/timber_bridge.json", """
            {
              "id": "timber_bridge", "category": "infrastructure", "era": 1, "footprint": [1, 1], "buildDays": 0,
              "cost": { "timber": 2 }, "roadCost": 800, "bridgeSpan": 3
            }
            """);
        content.Write("buildings/stone_storehouse.json", """
            {
              "id": "stone_storehouse", "category": "infrastructure", "era": 1, "footprint": [2, 2], "buildDays": 3,
              "cost": { "stone": 4 }, "upgradeOf": "storehouse", "storage": { "reach": 26, "carts": 3 }
            }
            """);
        content.Write("buildings/inn.json", """
            { "id": "inn", "category": "service", "era": 2, "footprint": [3, 3], "buildDays": 3, "lodging": 40 }
            """);
        content.Write("buildings/smelter.json", """
            { "id": "smelter", "category": "production", "era": 2, "footprint": [3, 3], "buildDays": 3, "attractiveness": -2 }
            """);
        content.Write("buildings/standing_stones.json", """
            {
              "id": "standing_stones", "category": "monument", "era": 1, "footprint": [3, 3], "buildDays": 12,
              "unique": true
            }
            """);
        content.Write("buildings/great_lighthouse.json", """
            {
              "id": "great_lighthouse", "category": "wonder", "era": 2, "footprint": [3, 3], "buildDays": 20,
              "placement": { "coastline": true }, "unique": true
            }
            """);

        content.Write("buildings/windmill.json", """
            {
              "id": "windmill", "category": "production", "era": 1, "footprint": [2, 2], "buildDays": 2,
              "siteBonus": [ { "when": { "terrain": ["hills"] }, "stat": "productivity", "percent": 15 } ]
            }
            """);
        content.Write("buildings/observatory.json", """
            {
              "id": "observatory", "category": "knowledge", "era": 2, "footprint": [3, 3], "buildDays": 6,
              "siteBonus": [ { "when": { "terrain": ["hills", "highlands"] }, "stat": "output.knowledge", "percent": 25 } ]
            }
            """);
        content.Write("buildings/apiary.json", """
            {
              "id": "apiary", "category": "production", "era": 2, "footprint": [3, 3], "buildDays": 2,
              "siteBonus": [
                { "when": { "near": { "tag": "orchard", "radius": 6 } }, "stat": "productivity", "percent": 20 },
                { "when": { "near": { "building": "grain_farm", "radius": 3 } }, "stat": "productivity", "percent": -5 }
              ]
            }
            """);
        content.Write("buildings/bathhouse.json", """
            {
              "id": "bathhouse", "category": "service", "era": 2, "footprint": [3, 3], "buildDays": 4,
              "aura": { "stat": "capacity", "percent": 10, "target": { "category": "residence" }, "coverage": 16 }
            }
            """);

        content.Write("monuments/standing_stones.json", """
            {
              "id": "standing_stones", "building": "standing_stones",
              "stages": [ { "stone": 20 }, { "stone": 30, "timber": 10 }, { "stone": 40 } ],
              "effects": [ { "grantPoints": { "culture": 50 } }, { "modifier": { "stat": "attractiveness", "percent": 5 } } ]
            }
            """);
        content.Write("monuments/great_lighthouse.json", """
            {
              "id": "great_lighthouse", "building": "great_lighthouse",
              "stages": [ { "stone": 40 }, { "timber": 20, "coin": 100 } ]
            }
            """);

        content.Write("festivals/harvest_fair.json", """
            {
              "id": "harvest_fair", "cost": { "bread": 20, "fish": 10 }, "days": 5, "culturePerDay": 5,
              "seasonBonus": { "season": "autumn", "percent": 50 },
              "effects": [ { "modifier": { "stat": "visitors", "percent": 100 } } ]
            }
            """);
        content.Write("festivals/midsummer_fair.json", """
            {
              "id": "midsummer_fair", "seasons": ["summer", "spring"], "cost": { "bread": 20 }, "days": 5,
              "culturePerDay": 10, "start": true
            }
            """);

        content.Write("happenings/travelling_scholar.json", """
            {
              "id": "travelling_scholar", "trigger": { "population": { "atLeast": 100 } },
              "chancePerMille": 20, "cooldownDays": 30,
              "options": [
                { "effects": [ { "grantPoints": { "knowledge": 40 } } ] },
                { "effects": [ { "modifier": { "stat": "productivity", "percent": 10 } } ], "durationDays": 10 },
                { "effects": [] }
              ],
              "defaultOption": 2
            }
            """);

        content.Write("techs/kitchen_tech.json", """
            {
              "id": "kitchen_tech", "era": 1, "cost": 100, "requires": ["net_weaving"],
              "effects": [
                { "unlockBuilding": "bakery" },
                { "unlockRecipe": "bake_bread" },
                { "unlockTier": "villagers" },
                { "unlockTradition": "common_hearth" },
                { "unlockFestival": "harvest_fair" },
                { "unlockTerrainAccess": "narrow_rivers" },
                { "unlockFeature": "coin" },
                { "unlockFeature": "great_works:writing" },
                { "traditionSlot": "craft" },
                { "modifier": {
                    "stat": "productivity", "target": { "tag": "farm" }, "percent": 20, "when": { "season": "autumn" } } },
                { "modifier": { "stat": "output.knowledge", "target": { "building": "scribes_hall" }, "percent": 20 } },
                { "modifier": { "stat": "constructionSpeed", "target": { "category": "production" }, "percent": -10 } },
                { "modifier": { "stat": "consumption", "target": { "good": "bread" }, "percent": 5 } },
                { "modifier": { "stat": "workforce", "target": { "tier": "villagers" }, "percent": 15 } },
                { "modifier": { "stat": "declineGrace", "percent": 50 } },
                { "grantGoods": { "timber": 10, "stone": 5.5 } },
                { "grantPoints": { "knowledge": 50, "coin": 2.5 } },
                { "revealCoastline": true },
                { "requestFestival": { "festival": "harvest_fair", "discountPercent": 25 } },
                { "modifier": { "stat": "output.culture", "target": { "discovery": "natural_wonder" }, "percent": 50 } },
                { "modifier": { "stat": "expeditionSpeed", "percent": 50 } },
                { "modifier": { "stat": "comfortBonus", "target": { "tier": "villagers" }, "percent": 50 } },
                { "modifier": { "stat": "festivalCost", "percent": -25 } },
                { "modifier": { "stat": "festivalDuration", "percent": 50 } },
                { "modifier": { "stat": "settlerArrival", "percent": 50, "when": { "season": "spring" } } }
              ],
              "repeatable": { "costGrowthPercent": 10 }
            }
            """);

        content.Write("civics/other_civic.json", """{ "id": "other_civic", "era": 1, "cost": 20 }""");
        content.Write("civics/kitchen_civic.json", """
            {
              "id": "kitchen_civic", "era": 1, "cost": 60, "requires": ["other_civic"],
              "inspiration": { "when": { "all": [
                { "count": { "building": "bakery", "atLeast": 2 } },
                { "count": { "tag": "farm", "terrain": "hills", "atLeast": 3 } },
                { "count": { "category": "knowledge", "atLeast": 1 } },
                { "count": { "terrain": "hills", "atLeast": 10 } },
                { "produced": { "good": "grain", "atLeast": 30 } },
                { "consumed": { "good": "bread", "atLeast": 12.5 } },
                { "population": { "tier": "villagers", "atLeast": 50 } },
                { "population": { "atLeast": 1500 } },
                { "roadTiles": { "atLeast": 40 } },
                { "roadTiles": { "road": "dirt_road", "atLeast": 60 } },
                { "expeditions": { "atLeast": 4 } },
                { "festivalsHeld": { "atLeast": 2 } },
                { "greatWorks": { "type": "writing", "displayed": true, "atLeast": 3 } },
                { "greatWorks": { "atLeast": 1 } },
                { "visitorsPerDay": { "atLeast": 200 } },
                { "researched": "net_weaving" },
                { "adopted": "other_civic" },
                { "discovered": { "kind": "iron_deposit", "atLeast": 1 } },
                { "discovered": { "region": "marsh", "atLeast": 3 } },
                { "season": "autumn" },
                { "era": "township" },
                { "stock": { "good": "timber", "atLeast": 40 } },
                { "any": [ { "season": "spring" }, { "not": { "season": "winter" } } ] }
              ] } }
            }
            """);

        content.Write("traditions/harvest_festival.json", """
            {
              "id": "harvest_festival", "slot": "craft",
              "effects": [ { "modifier": { "stat": "productivity", "target": { "tag": "farm" }, "percent": 20, "when": { "season": "autumn" } } } ]
            }
            """);
        content.Write("traditions/style_island_stone.json", """
            { "id": "style_island_stone", "slot": "heritage", "tags": ["style"], "style": "island_stone", "start": true }
            """);

        return content;
    }
}
