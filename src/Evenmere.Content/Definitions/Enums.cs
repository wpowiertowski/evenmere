namespace Evenmere.Content.Definitions;

// Closed vocabularies used inside content files. Each member is spelled in snake_case in JSON (ConcertHall is
// "concert_hall") unless a [ContentName] says otherwise; see ContentNames. Systems may branch on these.

/// <summary>What kind of material a good is, for grouping and display. Which residents need it comes from the tiers.</summary>
public enum GoodCategory
{
    /// <summary>Gathered from the land or sea.</summary>
    Raw,

    /// <summary>Processed from other goods and used to make more goods.</summary>
    Intermediate,

    /// <summary>Spent on construction.</summary>
    Construction,

    /// <summary>Consumed by residents, as a basic or a comfort need.</summary>
    Consumer,
}

/// <summary>What a building is for. Systems branch on this, and modifiers can target it.</summary>
public enum BuildingCategory
{
    /// <summary>Gathers or processes goods; it has a recipe.</summary>
    Production,

    /// <summary>Houses residents; its capacity and needs come from the tier.</summary>
    Residence,

    /// <summary>Roads, bridges, storehouses and halls.</summary>
    Infrastructure,

    /// <summary>Provides a service or lodging to the town.</summary>
    Service,

    /// <summary>Produces knowledge.</summary>
    Knowledge,

    /// <summary>Produces culture or hosts great works and festivals.</summary>
    Culture,

    /// <summary>Raises attractiveness: plazas, parks and lamps.</summary>
    Decoration,

    /// <summary>A multi-stage monument.</summary>
    Monument,

    /// <summary>A multi-stage wonder.</summary>
    Wonder,
}

/// <summary>A service a building provides within road range, which tiers can need.</summary>
public enum ServiceKind
{
    /// <summary>A market, which also gates access to goods.</summary>
    Market,

    /// <summary>A shrine.</summary>
    Shrine,

    /// <summary>A tavern.</summary>
    Tavern,

    /// <summary>A school.</summary>
    School,

    /// <summary>A theatre.</summary>
    Theatre,

    /// <summary>A concert hall.</summary>
    ConcertHall,

    /// <summary>Gas lighting.</summary>
    Gas,
}

/// <summary>A mineral deposit on the map that a gatherer can sit on.</summary>
public enum DepositKind
{
    /// <summary>A stone outcrop.</summary>
    Stone,

    /// <summary>A clay bank.</summary>
    Clay,

    /// <summary>An iron deposit.</summary>
    Iron,

    /// <summary>A coal seam.</summary>
    Coal,

    /// <summary>A copper deposit.</summary>
    Copper,
}

/// <summary>A crop with its own generated fertility layer.</summary>
public enum CropKind
{
    /// <summary>Grain.</summary>
    Grain,

    /// <summary>Flax.</summary>
    Flax,

    /// <summary>Hops.</summary>
    Hops,

    /// <summary>Apples.</summary>
    Apples,

    /// <summary>Grapes.</summary>
    Grapes,

    /// <summary>Tea.</summary>
    Tea,

    /// <summary>Madder.</summary>
    Madder,

    /// <summary>Sugar beet.</summary>
    SugarBeet,
}

/// <summary>A ground-opening flag that research unlocks.</summary>
public enum TerrainAccess
{
    /// <summary>Bridges over rivers up to 3 tiles wide.</summary>
    NarrowRivers,

    /// <summary>Bridges over rivers up to 8 tiles wide.</summary>
    WideRivers,

    /// <summary>Farms on hills.</summary>
    HillFarming,

    /// <summary>Draining marsh into fen.</summary>
    MarshDrainage,

    /// <summary>Roads and buildings on the highlands.</summary>
    Highlands,

    /// <summary>Buildings on deep water.</summary>
    DeepWater,

    /// <summary>Buildings beside reefs.</summary>
    Reefs,
}

/// <summary>A game feature that an effect can switch on. Great works are per type, so they carry a type ID too.</summary>
public enum FeatureKind
{
    /// <summary>Expeditions into fogged regions.</summary>
    Expeditions,

    /// <summary>The coin economy.</summary>
    Coin,

    /// <summary>Visitors.</summary>
    Visitors,

    /// <summary>Creating one type of great work; written <c>great_works:&lt;type&gt;</c>.</summary>
    GreatWorks,
}

/// <summary>The type of a tradition slot, and of the card that fits it.</summary>
public enum TraditionSlotType
{
    /// <summary>People and needs.</summary>
    Hearth,

    /// <summary>Production, logistics and construction.</summary>
    Craft,

    /// <summary>Culture, attractiveness and architectural style.</summary>
    Heritage,

    /// <summary>Accepts any card; Wild cards fit only Wild slots.</summary>
    Wild,
}

/// <summary>A stat that a modifier changes.</summary>
public enum Stat
{
    /// <summary>Production speed.</summary>
    [ContentName("productivity")]
    Productivity,

    /// <summary>How much residents consume.</summary>
    [ContentName("consumption")]
    Consumption,

    /// <summary>Construction speed.</summary>
    [ContentName("constructionSpeed")]
    ConstructionSpeed,

    /// <summary>House capacity.</summary>
    [ContentName("capacity")]
    Capacity,

    /// <summary>Workforce supply.</summary>
    [ContentName("workforce")]
    Workforce,

    /// <summary>Knowledge output.</summary>
    [ContentName("output.knowledge")]
    OutputKnowledge,

    /// <summary>Culture output.</summary>
    [ContentName("output.culture")]
    OutputCulture,

    /// <summary>Coin output.</summary>
    [ContentName("output.coin")]
    OutputCoin,

    /// <summary>Attractiveness.</summary>
    [ContentName("attractiveness")]
    Attractiveness,

    /// <summary>Visitor numbers.</summary>
    [ContentName("visitors")]
    Visitors,

    /// <summary>Cart speed.</summary>
    [ContentName("cartSpeed")]
    CartSpeed,

    /// <summary>Material cost of construction.</summary>
    [ContentName("materialCost")]
    MaterialCost,

    /// <summary>The decline grace period.</summary>
    [ContentName("declineGrace")]
    DeclineGrace,

    /// <summary>How fast expeditions travel.</summary>
    [ContentName("expeditionSpeed")]
    ExpeditionSpeed,

    /// <summary>The bonus each met comfort need adds to a tier's output.</summary>
    [ContentName("comfortBonus")]
    ComfortBonus,

    /// <summary>The goods a festival costs to hold.</summary>
    [ContentName("festivalCost")]
    FestivalCost,

    /// <summary>How many days festivals last.</summary>
    [ContentName("festivalDuration")]
    FestivalDuration,

    /// <summary>How quickly new settlers arrive.</summary>
    [ContentName("settlerArrival")]
    SettlerArrival,

    /// <summary>How many workers a building needs: negative reduces the crew (ADR-0015). Appended last so existing serialised values keep their order.</summary>
    [ContentName("crew")]
    Crew,
}

/// <summary>A kind of point that can be granted.</summary>
public enum PointKind
{
    /// <summary>Knowledge points, which pay for techs.</summary>
    Knowledge,

    /// <summary>Culture points, which pay for civics.</summary>
    Culture,

    /// <summary>Coin.</summary>
    Coin,
}

/// <summary>Something an expedition can discover, for the <c>discovered</c> condition.</summary>
public enum DiscoveryKind
{
    /// <summary>A stone outcrop.</summary>
    StoneDeposit,

    /// <summary>A clay bank.</summary>
    ClayDeposit,

    /// <summary>An iron deposit.</summary>
    IronDeposit,

    /// <summary>A coal seam.</summary>
    CoalDeposit,

    /// <summary>A copper deposit.</summary>
    CopperDeposit,

    /// <summary>A natural wonder.</summary>
    NaturalWonder,

    /// <summary>Ruins.</summary>
    Ruin,

    /// <summary>A shipwreck.</summary>
    Shipwreck,
}

/// <summary>How a gatherer's yield is measured from the map.</summary>
public enum AreaYieldKind
{
    /// <summary>The share of tiles of some terrains within a radius, optionally not claimed by other buildings.</summary>
    Radius,

    /// <summary>The share of footprint tiles fertile for the placement's crop.</summary>
    FootprintFertility,

    /// <summary>The share of footprint tiles over the placement's deposit.</summary>
    FootprintDeposit,
}

/// <summary>Where the works of a great-work type come from.</summary>
public enum GreatWorkSource
{
    /// <summary>Residents and buildings accumulate creativity, and a work is created at a threshold.</summary>
    Creativity,

    /// <summary>The works are found by discovery, such as relics from ruins.</summary>
    Discovery,
}
