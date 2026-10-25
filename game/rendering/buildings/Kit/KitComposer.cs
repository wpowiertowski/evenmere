namespace Evenmere.Game.Rendering.Buildings.Kit;

/// <summary>
/// Interprets a <see cref="KitSpec"/> into <see cref="KitBuilder"/> calls. It branches only on part kinds, never on
/// building IDs, so new specs compose new buildings with no code changes; data that names an unknown token already
/// fell back to a default during <see cref="KitSpec.Load"/>.
/// </summary>
public static class KitComposer
{
    /// <summary>Composes the spec's parts into the builder, from a cleared state.</summary>
    /// <param name="builder">The builder receiving the parts.</param>
    /// <param name="spec">The spec to compose.</param>
    public static void Build(KitBuilder builder, KitSpec spec)
    {
        builder.Begin();
        foreach (KitPart part in spec.Parts)
        {
            Add(builder, part);
        }
    }

    /// <summary>Builds the spec and returns the height it reaches, for scaffold sizing.</summary>
    /// <param name="builder">The builder receiving the parts.</param>
    /// <param name="spec">The spec to compose.</param>
    /// <returns>The highest Y written, in tile units.</returns>
    public static float BuildHeight(KitBuilder builder, KitSpec spec)
    {
        Build(builder, spec);
        return builder.MaxY;
    }

    private static void Add(KitBuilder b, KitPart part)
    {
        switch (part)
        {
            case FootingPart p:
                b.Footing(p.X0, p.Z0, p.X1, p.Z1, p.H, Slot(p.Material));
                break;
            case WallsPart p:
                b.Walls(p.X0, p.Z0, p.X1, p.Z1, p.H, Slot(p.Material), p.Posts, Slot("wood.beam.dark"));
                break;
            case GableRoofPart p:
                b.GableRoof(p.X0, p.X1, p.Z0, p.Z1, p.WallTop, p.RidgeTop, Slot(p.Roof), Slot(p.Gable));
                break;
            case HipRoofPart p:
                b.HipRoof(p.X0, p.X1, p.Z0, p.Z1, p.WallTop, p.RidgeTop, Slot(p.Roof));
                break;
            case ShedRoofPart p:
                b.ShedRoof(p.X0, p.X1, p.Z0, p.Z1, p.TopHigh, p.TopLow, Slot(p.Roof), Slot(p.Cheek));
                break;
            case PyramidCapPart p:
                b.PyramidCap(p.X0, p.Z0, p.X1, p.Z1, p.BaseY, p.ApexY, Slot(p.Material));
                break;
            case ChimneyPart p:
                b.Chimney(p.X, p.Z, p.BaseY, p.TopY, Slot(p.Material), Slot("stone.dark"));
                break;
            case DoorPart p:
                b.Door(p.X, p.Z, p.W, p.H, p.Face, Slot(p.Material), Slot("wood.beam.dark"));
                break;
            case WindowPart p:
                b.Window(p.X, p.Y, p.Z, p.W, p.H, p.Face, p.Shutters, p.Flowers, Slot("wood.timber.light"), Slot("wood.plank.mid"));
                break;
            case PostsPart p:
                b.Posts(p.X0, p.Z0, p.X1, p.Z1, p.H, Slot(p.Material));
                break;
            case LogPilePart p:
                b.LogPile(p.X, p.Z, p.Len, p.Axis, Slot("wood.bark.dark"), Slot("wood.timber.light"));
                break;
            case PlankStackPart p:
                b.PlankStack(p.X, p.Z, p.W, p.H, p.Axis, Slot(p.Material));
                break;
            case CratePart p:
                int levels = Math.Clamp(p.Count, 1, 3);
                for (int level = 0; level < levels; level++)
                {
                    b.Crate(p.X, p.Z, level * p.S, p.S, Slot(p.Material), Slot("wood.beam.dark"));
                }

                break;
            case SackPart p:
                b.Sack(p.X, p.Z, p.S, Slot(p.Material));
                break;
            case BarrelPart p:
                b.Barrel(p.X, p.Z, Slot("wood.timber.light"), Slot("wood.beam.dark"));
                break;
            case CartPart p:
                b.Cart(p.X, p.Z, p.Rot, Slot("wood.timber.light"), Slot("wood.bark.dark"));
                break;
            case NetFramePart p:
                b.NetFrame(p.X, p.Z, p.W, p.H, Slot("wood.beam.dark"), Slot("prop.net"));
                break;
            case DryingRackPart p:
                b.DryingRack(p.X, p.Z, p.Len, p.Axis, Slot("wood.beam.dark"), Slot(p.Blanks));
                break;
            case FencePart p:
                b.Fence(p.X0, p.Z0, p.X1, p.Z1, Gap(p.Gap), Slot("wood.beam.dark"), Slot("wood.plank.mid"));
                break;
            case CropRowsPart p:
                b.CropRows(p.X0, p.Z0, p.X1, p.Z1, Math.Clamp(p.Rows, 1, 12), Slot(p.Crop), Slot(p.Soil), Slot("field.fallow"));
                break;
            case SailsPart p:
                b.Sails(p.X, p.Y, p.Z, p.Radius, Slot("wood.beam.dark"), Slot("prop.sail"));
                break;
            case OvenPart p:
                b.Oven(p.X, p.Z, p.R, Slot("brick.fired"), Slot("stone.dark"));
                break;
            case KilnStackPart p:
                b.KilnStack(p.X, p.Z, Slot("brick.fired"), Slot("stone.dark"));
                break;
            case BannerPart p:
                b.BannerPole(p.X, p.Z, p.H, Slot("wood.beam.dark"), Slot(p.Material));
                break;
            case BellFramePart p:
                b.BellFrame(p.X, p.Z, Slot("wood.beam.dark"), Slot("stone.dark"));
                break;
            case BowlsPart p:
                b.OfferingBowls(p.X, p.Z, p.Lit, Slot("pottery.glaze"), Slot("paper.highlight"));
                break;
            case DerrickPart p:
                b.Derrick(p.X, p.Z, p.H, Slot("wood.beam.dark"), Slot("prop.rope"), Slot("stone.light"));
                break;
            case StoneBlocksPart p:
                b.StoneBlocks(p.X, p.Z, Math.Clamp(p.Count, 1, 8), p.Axis, Slot(p.Material));
                break;
            case SpoilHeapPart p:
                b.SpoilHeap(p.X, p.Z, p.R, p.H, Slot(p.Material));
                break;
            case TreePart p:
                b.Tree(p.X, p.Z, Slot("wood.bark.dark"), Slot(p.Canopy), p.Fruit, Slot("good.apples"));
                break;
            case SheepPart p:
                b.Sheep(p.X, p.Z, p.Rot, Slot("wall.plaster"));
                break;
            case HayrickPart p:
                b.Hayrick(p.X, p.Z, p.R, Slot("roof.thatch.light"));
                break;
            case StookPart p:
                b.Stook(p.X, p.Z, Slot("field.grain"));
                break;
            case ScarecrowPart p:
                b.Scarecrow(p.X, p.Z, Slot("wood.beam.dark"), Slot("field.grain"));
                break;
            case StoryPostPart p:
                b.StoryPost(p.X, p.Z, p.H, Slot("wood.beam.dark"));
                break;
            case BenchLogPart p:
                b.BenchLog(p.X, p.Z, p.Len, p.Axis, Slot("wood.bark.dark"));
                break;
            case FirePitPart p:
                b.FirePit(p.X, p.Z, p.R, p.Lit, Slot("stone.mid"), Slot("wood.bark.dark"), Slot("paper.highlight"));
                break;
            case MaypolePart p:
                b.Maypole(p.X, p.Z, p.H, p.CX0, p.CZ0, p.CX1, p.CZ1, p.PostH, Slot("wood.beam.dark"), Slot("field.grain"), Slot("ui.accent"), Slot("roof.canvas"));
                break;
            case MedallionPart p:
                b.Medallion(p.X, p.Z, p.R, Slot("brick.fired"), Slot("field.grain"));
                break;
            case BenchSlabPart p:
                b.BenchSlab(p.X, p.Z, p.Len, p.Axis, Slot("stone.mid"));
                break;
            case PlanterPart p:
                b.Planter(p.X, p.Z, Slot("wood.beam.dark"), Slot("leaf.mid"), Slot("ui.accent"));
                break;
            case CanoePart p:
                b.Canoe(p.X, p.Z, p.Len, p.Axis, Slot("wood.beam.dark"), Slot("wood.timber.light"));
                break;
            case ParchmentLinePart p:
                b.ParchmentLine(p.X0, p.Z0, p.X1, p.Z1, Slot("wood.beam.dark"), Slot("paper.highlight"));
                break;
            case JettyPart p:
                b.Jetty(p.X0, p.Z0, p.X1, p.Z1, Slot("wood.plank.mid"), Slot("wood.bark.dark"));
                break;
            case CoraclePart p:
                b.Coracle(p.X, p.Z, Slot("wood.timber.light"));
                break;
            case PuddlePart p:
                b.Puddle(p.X, p.Z, p.R, Slot("water.river"));
                break;
            case MoundPart p:
                b.Mound(p.X0, p.Z0, p.X1, p.Z1, p.H, Slot("leaf.fen"));
                break;
            case StandingStonePart p:
                b.StandingStone(p.X, p.Z, p.W, p.H, p.D, Slot("stone.mid"), Slot("stone.light"));
                break;
            case TrilithonPart p:
                b.Trilithon(p.X, p.Z, Slot("stone.mid"), Slot("stone.light"));
                break;
            case GroundPart p:
                b.Ground(p.X0, p.Z0, p.X1, p.Z1, p.Y, Slot(p.Material));
                break;
            case StallTablePart p:
                b.StallTable(p.X, p.Z, p.Rot, Slot("wood.timber.light"), Slot(p.Goods));
                break;
        }
    }

    private static byte Slot(string token) =>
        KitPalette.TryGetSlot(token, out byte slot) ? slot : (byte)0;

    private static byte Gap(string gap) => gap switch
    {
        "N" => 1,
        "S" => 2,
        "W" => 3,
        "E" => 4,
        _ => 0,
    };
}
