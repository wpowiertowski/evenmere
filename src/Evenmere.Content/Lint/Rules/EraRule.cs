using Evenmere.Content.Definitions;
using Evenmere.Core.Ids;

namespace Evenmere.Content.Lint.Rules;

/// <summary>
/// EVL008 (ADR-0010): era N+1 begins when the tech gateway and the civic gateway of era N are complete.
/// <list type="bullet">
/// <item><description>For each pair of consecutive eras, the later era's <c>requires</c> names a tech and a civic that have
/// <c>gateway: true</c> and belong to the earlier era. The first era requires nothing.</description></item>
/// <item><description>A node with <c>gateway: true</c> that no era requires is an error, so each transition has exactly one
/// gateway per tree.</description></item>
/// <item><description>No tech or civic requires a node of a later era.</description></item>
/// </list>
/// </summary>
internal static class EraRule
{
    public static void Run(LintContext context)
    {
        ContentDb db = context.Db;
        bool[] techNamed = new bool[db.Techs.Count];
        bool[] civicNamed = new bool[db.Civics.Count];

        for (int i = 0; i < db.ErasByOrder.Length; i++)
        {
            EraDef era = db.Get(db.ErasByOrder[i]);
            string eraName = db.Eras.NameOf(era.Id);
            string file = context.FileOf(ContentType.Eras, eraName);

            if (i == 0)
            {
                if (era.RequiresTech is { } firstTech && era.RequiresCivic is { } firstCivic)
                {
                    // Counted as referenced, so the gateways it names are not also reported as unreferenced.
                    techNamed[firstTech.Value] = true;
                    civicNamed[firstCivic.Value] = true;
                    context.Error(
                        LintCodes.Evl008,
                        file,
                        LintContext.Pointer("requires"),
                        $"era '{eraName}' is the first era and begins at the start, so it cannot require gateways");
                }

                continue;
            }

            EraDef earlier = db.Get(db.ErasByOrder[i - 1]);
            if (era.RequiresTech is not { } techId || era.RequiresCivic is not { } civicId)
            {
                context.Error(
                    LintCodes.Evl008,
                    file,
                    string.Empty,
                    $"era '{eraName}' (era {era.Order}) has no \"requires\"; it begins when a tech gateway and a civic gateway of era {earlier.Order} are complete");
                continue;
            }

            techNamed[techId.Value] = true;
            civicNamed[civicId.Value] = true;

            TechDef tech = db.Get(techId);
            CheckGateway(context, file, "tech", db.Techs.NameOf(techId), tech.Gateway, tech.Era, eraName, earlier.Order);
            CivicDef civic = db.Get(civicId);
            CheckGateway(context, file, "civic", db.Civics.NameOf(civicId), civic.Gateway, civic.Era, eraName, earlier.Order);
        }

        for (ushort i = 0; i < db.Techs.Count; i++)
        {
            if (db.Techs.Items[i].Gateway && !techNamed[i])
            {
                ReportUnreferenced(context, ContentType.Techs, "tech", db.Techs.Names[i]);
            }
        }

        for (ushort i = 0; i < db.Civics.Count; i++)
        {
            if (db.Civics.Items[i].Gateway && !civicNamed[i])
            {
                ReportUnreferenced(context, ContentType.Civics, "civic", db.Civics.Names[i]);
            }
        }

        CheckRequirementsEra(context);
    }

    private static void CheckGateway(LintContext context, string eraFile, string noun, string id, bool isGateway, int nodeEra, string eraName, int earlierOrder)
    {
        string pointer = LintContext.Pointer("requires", noun);
        if (!isGateway)
        {
            context.Error(
                LintCodes.Evl008,
                eraFile,
                pointer,
                $"era '{eraName}' requires {noun} '{id}', which is not a gateway; set \"gateway\": true on it");
        }

        if (nodeEra != earlierOrder)
        {
            context.Error(
                LintCodes.Evl008,
                eraFile,
                pointer,
                $"era '{eraName}' requires {noun} '{id}', which belongs to era {nodeEra}; the gateway into era '{eraName}' must belong to the previous era, era {earlierOrder}");
        }
    }

    private static void ReportUnreferenced(LintContext context, ContentType type, string noun, string id) =>
        context.Error(
            LintCodes.Evl008,
            context.FileOf(type, id),
            LintContext.Pointer("gateway"),
            $"{noun} '{id}' is a gateway, but no era requires it; each era transition has exactly one tech gateway and one civic gateway");

    private static void CheckRequirementsEra(LintContext context)
    {
        ContentDb db = context.Db;
        for (int i = 0; i < db.Techs.Count; i++)
        {
            TechDef tech = db.Techs.Items[i];
            for (int r = 0; r < tech.Requires.Length; r++)
            {
                TechDef required = db.Get(tech.Requires[r]);
                if (required.Era > tech.Era)
                {
                    context.Error(
                        LintCodes.Evl008,
                        context.FileOf(ContentType.Techs, db.Techs.Names[i]),
                        LintContext.ItemPointer("requires", r),
                        $"tech '{db.Techs.Names[i]}' (era {tech.Era}) requires '{db.Techs.NameOf(tech.Requires[r])}', which belongs to the later era {required.Era}");
                }
            }
        }

        for (int i = 0; i < db.Civics.Count; i++)
        {
            CivicDef civic = db.Civics.Items[i];
            for (int r = 0; r < civic.Requires.Length; r++)
            {
                CivicDef required = db.Get(civic.Requires[r]);
                if (required.Era > civic.Era)
                {
                    context.Error(
                        LintCodes.Evl008,
                        context.FileOf(ContentType.Civics, db.Civics.Names[i]),
                        LintContext.ItemPointer("requires", r),
                        $"civic '{db.Civics.Names[i]}' (era {civic.Era}) requires '{db.Civics.NameOf(civic.Requires[r])}', which belongs to the later era {required.Era}");
                }
            }
        }
    }
}
