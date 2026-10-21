using Evenmere.Content;
using Evenmere.Content.Definitions;

namespace Evenmere.Sim.Systems.Population;

/// <summary>
/// How a tier's needs map to the bits of a house's masks: basic goods first, then basic services, each in the order of the
/// tier's definition; comfort goods have a mask of their own. Shared by the Needs system, which sets the bits, and the
/// Population system and the queries, which read them.
/// </summary>
internal static class NeedLayout
{
    /// <summary>The most needs one mask can hold.</summary>
    public const int MaxNeeds = 32;

    /// <summary>Gets how many basic needs a tier has: its basic goods plus its basic services.</summary>
    /// <param name="tier">The tier.</param>
    /// <returns>The number of basic needs.</returns>
    public static int BasicCount(TierDef tier) => tier.BasicGoods.Length + tier.BasicServices.Length;

    /// <summary>Gets a mask with the lowest <paramref name="count"/> bits set.</summary>
    /// <param name="count">How many bits, from 0 to <see cref="MaxNeeds"/>.</param>
    /// <returns>The mask.</returns>
    public static uint MaskOf(int count) => count >= MaxNeeds ? uint.MaxValue : (1u << count) - 1u;

    /// <summary>Checks that every tier's needs fit in the masks, so a bit index never overflows.</summary>
    /// <param name="content">The loaded content.</param>
    /// <exception cref="InvalidDataException">A tier has more than <see cref="MaxNeeds"/> basic needs or comfort goods.</exception>
    public static void Validate(ContentDb content)
    {
        ArgumentNullException.ThrowIfNull(content);

        for (int i = 0; i < content.Tiers.Count; i++)
        {
            TierDef tier = content.Tiers.Items[i];
            if (BasicCount(tier) > MaxNeeds || tier.ComfortGoods.Length > MaxNeeds)
            {
                throw new InvalidDataException(
                    $"Tier '{content.Tiers.Names[i]}' has more than {MaxNeeds} basic needs or comfort goods; a house's need masks hold {MaxNeeds} bits.");
            }
        }
    }
}
