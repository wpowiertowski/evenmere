using System.Collections.Immutable;
using System.Text.Json;
using Evenmere.Content.Conditions;
using Evenmere.Content.Definitions;
using Evenmere.Content.Effects;
using Evenmere.Core.Ids;
using Evenmere.Core.Numerics;

namespace Evenmere.Content.Loading;

// Cost objects, the single-key effect union and modifier targets.
internal sealed partial class ItemReader
{
    /// <summary>Reads a bill of materials object; the key <c>coin</c> is coin rather than a good.</summary>
    public CostSpec ReadCostObject(JsonElement element, string pointer, bool allowFree)
    {
        using ObjectView? o = OpenObject(element, pointer);
        if (o is null)
        {
            return CostSpec.Free;
        }

        ImmutableArray<GoodAmount> goods = o.ReadGoodAmounts(coinIsReserved: true, out Amount coin);
        var cost = new CostSpec(goods, coin);
        if (!allowFree && cost.IsFree)
        {
            Error(pointer, "a cost must ask for at least one good or some coin");
        }

        return cost;
    }

    /// <summary>Reads an array of effects. Each is an object with exactly one key; an unknown key is a diagnostic.</summary>
    public ImmutableArray<Effect> ReadEffects(JsonElement array, string pointer)
    {
        ImmutableArray<Effect>.Builder effects = ImmutableArray.CreateBuilder<Effect>();
        if (array.ValueKind != JsonValueKind.Array)
        {
            Error(pointer, $"expected an array of effects but found {Describe(array)}");
            return effects.ToImmutable();
        }

        int index = 0;
        foreach (JsonElement item in array.EnumerateArray())
        {
            Effect? effect = ReadEffect(item, JsonPointer.Append(pointer, index));
            if (effect is not null)
            {
                effects.Add(effect);
            }

            index++;
        }

        return effects.ToImmutable();
    }

    private Effect? ReadEffect(JsonElement element, string pointer)
    {
        if (!TrySingleKey(element, pointer, "an effect", out string kind, out JsonElement value, out string at))
        {
            return null;
        }

        switch (kind)
        {
            case "unlockBuilding":
                return TryRef(value, at, Tables.Buildings, out BuildingTypeId building) ? new UnlockBuildingEffect(building) : null;
            case "unlockRecipe":
                return TryRef(value, at, Tables.Recipes, out RecipeId recipe) ? new UnlockRecipeEffect(recipe) : null;
            case "unlockTier":
                return TryRef(value, at, Tables.Tiers, out TierId tier) ? new UnlockTierEffect(tier) : null;
            case "unlockTradition":
                return TryRef(value, at, Tables.Traditions, out TraditionId tradition) ? new UnlockTraditionEffect(tradition) : null;
            case "unlockFestival":
                return TryRef(value, at, Tables.Festivals, out FestivalId festival) ? new UnlockFestivalEffect(festival) : null;
            case "unlockTerrainAccess":
                return TryEnum(value, at, out TerrainAccess access) ? new UnlockTerrainAccessEffect(access) : null;
            case "unlockFeature":
                return ReadFeature(value, at);
            case "traditionSlot":
                return TryEnum(value, at, out TraditionSlotType slot) ? new TraditionSlotEffect(slot) : null;
            case "modifier":
                return ReadModifier(value, at);
            case "grantGoods":
                return ReadGrantGoods(value, at);
            case "grantPoints":
                return ReadGrantPoints(value, at);
            case "revealCoastline":
                return ReadRevealCoastline(value, at);
            case "requestFestival":
                return ReadRequestFestival(value, at);
            default:
                Error(at, $"unknown effect '{kind}'; expected one of: {string.Join(", ", ContentNames.EffectKinds)}");
                return null;
        }
    }

    private UnlockFeatureEffect? ReadFeature(JsonElement value, string pointer)
    {
        if (!TryString(value, pointer, out string text))
        {
            return null;
        }

        const string GreatWorksPrefix = "great_works:";
        if (text.StartsWith(GreatWorksPrefix, StringComparison.Ordinal))
        {
            return TryRefName(text[GreatWorksPrefix.Length..], pointer, Tables.GreatWorks, out GreatWorkTypeId type)
                ? new UnlockFeatureEffect(FeatureKind.GreatWorks, type)
                : null;
        }

        if (ContentNames.TryParse(text, out FeatureKind feature) && feature != FeatureKind.GreatWorks)
        {
            return new UnlockFeatureEffect(feature, null);
        }

        Error(pointer, text == ContentNames.NameOf(FeatureKind.GreatWorks)
            ? "write the great-work type after the colon: great_works:<type>"
            : $"'{text}' is not a feature; expected one of: expeditions, coin, visitors, great_works:<type>");
        return null;
    }

    private ModifierEffect? ReadModifier(JsonElement value, string pointer)
    {
        using ObjectView? o = OpenObject(value, pointer);
        if (o is null)
        {
            return null;
        }

        int errorsBefore = ErrorCount;
        Stat stat = o.ReqEnum<Stat>("stat");
        int percent = o.ReqInt("percent", -1000, 10000);

        ModifierTarget target = new IslandTarget();
        if (o.TryGet("target", out JsonElement targetElement))
        {
            target = ReadTarget(targetElement, o.Child("target")) ?? target;
        }

        Condition? when = null;
        if (o.TryGet("when", out JsonElement whenElement))
        {
            when = ReadCondition(whenElement, o.Child("when"));
        }

        return ErrorCount == errorsBefore ? new ModifierEffect(stat, target, percent, when) : null;
    }

    private RevealCoastlineEffect? ReadRevealCoastline(JsonElement value, string pointer)
    {
        if (value.ValueKind == JsonValueKind.True)
        {
            return new RevealCoastlineEffect();
        }

        Error(pointer, $"revealCoastline must be exactly true, but found {Describe(value)}");
        return null;
    }

    private RequestFestivalEffect? ReadRequestFestival(JsonElement value, string pointer)
    {
        using ObjectView? o = OpenObject(value, pointer);
        if (o is null)
        {
            return null;
        }

        int errorsBefore = ErrorCount;
        FestivalId festival = o.ReqRef(Tables.Festivals, "festival");
        int discount = o.ReqInt("discountPercent", 1, 100);
        return ErrorCount == errorsBefore ? new RequestFestivalEffect(festival, discount) : null;
    }

    /// <summary>
    /// Reads a modifier target: an object with exactly one key from <see cref="ContentNames.ModifierTargetKinds"/>.
    /// Callers that allow island-wide leave the property out instead.
    /// </summary>
    public ModifierTarget? ReadTarget(JsonElement element, string pointer)
    {
        if (!TrySingleKey(element, pointer, "a modifier target", out string kind, out JsonElement value, out string at))
        {
            return null;
        }

        switch (kind)
        {
            case "building":
                return TryRef(value, at, Tables.Buildings, out BuildingTypeId building)
                    ? new BuildingsTarget(new BuildingTypeSelector(building))
                    : null;
            case "tag":
                return TryTag(value, at, out string tag) ? new BuildingsTarget(new BuildingTagSelector(tag)) : null;
            case "category":
                return TryEnum(value, at, out BuildingCategory category)
                    ? new BuildingsTarget(new BuildingCategorySelector(category))
                    : null;
            case "tier":
                return TryRef(value, at, Tables.Tiers, out TierId tier) ? new TierTarget(tier) : null;
            case "good":
                return TryRef(value, at, Tables.Goods, out GoodId good) ? new GoodTarget(good) : null;
            case "discovery":
                return ReadDiscoveryTarget(value, at);
            default:
                Error(at, $"unknown modifier target '{kind}'; expected one of: {string.Join(", ", ContentNames.ModifierTargetKinds)}");
                return null;
        }
    }

    private DiscoveryTarget? ReadDiscoveryTarget(JsonElement value, string pointer)
    {
        if (!TryString(value, pointer, out string text))
        {
            return null;
        }

        if (ContentNames.TryParse(text, out DiscoveryKind kind) && ContentNames.DiscoveryTargetKinds.Contains(text))
        {
            return new DiscoveryTarget(kind);
        }

        Error(pointer, $"'{text}' is not a discovery a modifier can target; expected one of: {string.Join(", ", ContentNames.DiscoveryTargetKinds)}");
        return null;
    }

    private GrantGoodsEffect? ReadGrantGoods(JsonElement value, string pointer)
    {
        using ObjectView? map = OpenObject(value, pointer);
        if (map is null)
        {
            return null;
        }

        int errorsBefore = ErrorCount;
        ImmutableArray<GoodAmount> goods = map.ReadGoodAmounts(coinIsReserved: false, out _);
        if (goods.IsEmpty && ErrorCount == errorsBefore)
        {
            Error(pointer, "grantGoods must name at least one good");
        }

        return ErrorCount == errorsBefore ? new GrantGoodsEffect(goods) : null;
    }

    private GrantPointsEffect? ReadGrantPoints(JsonElement value, string pointer)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            Error(pointer, $"expected an object but found {Describe(value)}");
            return null;
        }

        int errorsBefore = ErrorCount;
        var points = new List<PointAmount>();
        foreach (JsonProperty property in value.EnumerateObject())
        {
            string at = JsonPointer.Append(pointer, property.Name);
            if (!ContentNames.TryParse(property.Name, out PointKind kind))
            {
                Error(at, $"'{property.Name}' is not a kind of point; expected one of: {string.Join(", ", ContentNames.NamesOf<PointKind>())}");
            }
            else if (TryAmount(property.Value, at, QuantityRange.Positive, out Amount amount))
            {
                points.Add(new PointAmount(kind, amount));
            }
        }

        if (points.Count == 0 && ErrorCount == errorsBefore)
        {
            Error(pointer, "grantPoints must name at least one kind of point");
        }

        points.Sort(static (a, b) => a.Kind.CompareTo(b.Kind));
        return ErrorCount == errorsBefore ? new GrantPointsEffect([.. points]) : null;
    }

    /// <summary>Reads a snake_case tag.</summary>
    public bool TryTag(JsonElement value, string pointer, out string tag)
    {
        if (TryString(value, pointer, out tag))
        {
            if (Ids.IsValid(tag))
            {
                return true;
            }

            Error(pointer, $"'{tag}' is not a valid tag; use snake_case");
        }

        return false;
    }

    /// <summary>Checks that a value is an object with exactly one key and returns that key and its value.</summary>
    private bool TrySingleKey(JsonElement element, string pointer, string what, out string key, out JsonElement value, out string valuePointer)
    {
        key = string.Empty;
        value = default;
        valuePointer = pointer;

        if (element.ValueKind != JsonValueKind.Object)
        {
            Error(pointer, $"expected {what}, an object with exactly one key, but found {Describe(element)}");
            return false;
        }

        var keys = new List<string>();
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (keys.Count == 0)
            {
                value = property.Value;
            }

            keys.Add(property.Name);
        }

        if (keys.Count != 1)
        {
            Error(pointer, keys.Count == 0
                ? $"{what} must have exactly one key but the object is empty"
                : $"{what} must have exactly one key but has {keys.Count}: {string.Join(", ", keys)}");
            return false;
        }

        key = keys[0];
        valuePointer = JsonPointer.Append(pointer, key);
        return true;
    }
}
