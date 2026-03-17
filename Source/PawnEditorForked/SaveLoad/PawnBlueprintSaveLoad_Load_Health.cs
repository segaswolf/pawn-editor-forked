using System;
using System.Linq;
using System.Xml;
using RimWorld;
using Verse;

namespace PawnEditor;

/// <summary>
/// Partial — Load: Health section.
/// Hediffs (including implants/prosthetics), Abilities, Apparel, Equipment.
/// </summary>
public static partial class PawnBlueprintSaveLoad
{
    // ── Load: Hediffs ──

    private static void LoadHediffs(Pawn pawn, XmlNode root)
    {
        if (pawn.health?.hediffSet == null) return;
        var hediffsNode = root.SelectSingleNode("hediffs");
        if (hediffsNode == null) return;
        try
        {
            foreach (XmlNode li in hediffsNode.SelectNodes("li"))
            {
                if (!IsAvailable(li)) continue;
                var defName = li.Attributes?["defName"]?.Value;
                if (defName.NullOrEmpty()) continue;
                var def = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
                if (def == null) { Warn($"Hediff '{defName}' not found, skipping"); continue; }

                // Resolve body part, using label for left/right disambiguation
                BodyPartRecord part = null;
                var bodyPartDefName = li.Attributes?["bodyPart"]?.Value;
                var bodyPartLabel   = li.Attributes?["bodyPartLabel"]?.Value;
                if (!bodyPartDefName.NullOrEmpty())
                {
                    var bpDef = DefDatabase<BodyPartDef>.GetNamedSilentFail(bodyPartDefName);
                    if (bpDef != null)
                    {
                        var candidates = pawn.health.hediffSet.GetNotMissingParts()
                            .Where(p => p.def == bpDef).ToList();
                        if (!bodyPartLabel.NullOrEmpty())
                            part = candidates.FirstOrDefault(p => p.Label == bodyPartLabel) ?? candidates.FirstOrDefault();
                        else
                            part = candidates.FirstOrDefault();
                    }
                }
                if (bodyPartDefName != null && part == null) continue; // Body part gone — skip

                try
                {
                    // Implants/prosthetics need RestorePart first to clear any damage/missing state
                    bool isImplant = li.Attributes?["isImplant"]?.Value == "true";
                    if (isImplant && part != null)
                    {
                        try { pawn.health.RestorePart(part, null, checkStateChange: false); }
                        catch { /* Part may already be healthy */ }
                    }

                    var hediff = HediffMaker.MakeHediff(def, pawn, part);
                    var severityStr = li.Attributes?["severity"]?.Value;
                    if (!severityStr.NullOrEmpty() && float.TryParse(severityStr,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var sev))
                        hediff.Severity = sev;

                    // Preserve permanent scar state
                    if (li.Attributes?["isPermanent"]?.Value == "true" && hediff is HediffWithComps hwc)
                    {
                        var permComp = hwc.TryGetComp<HediffComp_GetsPermanent>();
                        if (permComp != null) permComp.IsPermanent = true;
                    }

                    var ageTicksStr = li.Attributes?["ageTicks"]?.Value;
                    if (!ageTicksStr.NullOrEmpty() && int.TryParse(ageTicksStr, out var ageTicks))
                        hediff.ageTicks = ageTicks;

                    pawn.health.hediffSet.AddDirect(hediff);
                }
                catch (Exception ex) { Warn($"Hediff '{defName}': {ex.Message}"); }
            }
        }
        catch (Exception ex) { Warn($"Hediffs: {ex.Message}"); }
    }

    // ── Load: Abilities ──

    private static void LoadAbilities(Pawn pawn, XmlNode root)
    {
        if (pawn.abilities == null) return;
        var abNode = root.SelectSingleNode("abilities");
        if (abNode == null) return;
        try
        {
            foreach (XmlNode li in abNode.SelectNodes("li"))
            {
                if (!IsAvailable(li)) continue;
                var defName = li.Attributes?["defName"]?.Value;
                if (defName.NullOrEmpty()) continue;
                var def = DefDatabase<AbilityDef>.GetNamedSilentFail(defName);
                if (def == null) { Warn($"Ability '{defName}' not found, skipping"); continue; }
                if (pawn.abilities.GetAbility(def) == null)
                    pawn.abilities.GainAbility(def);
            }
        }
        catch (Exception ex) { Warn($"Abilities: {ex.Message}"); }
    }

    // ── Load: Apparel & Equipment ──

    private static void LoadApparel(Pawn pawn, XmlNode root)
    {
        var apparelNode = root.SelectSingleNode("apparel");
        if (apparelNode != null && pawn.apparel != null)
        {
            try
            {
                foreach (XmlNode li in apparelNode.SelectNodes("li"))
                {
                    if (!IsAvailable(li)) continue;
                    var defName = li.Attributes?["defName"]?.Value;
                    if (defName.NullOrEmpty()) continue;
                    var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                    if (def == null) { Warn($"Apparel '{defName}' not found, skipping"); continue; }

                    ThingDef stuffDef = null;
                    var stuffName = li.Attributes?["stuff"]?.Value;
                    if (!stuffName.NullOrEmpty())
                        stuffDef = DefDatabase<ThingDef>.GetNamedSilentFail(stuffName);

                    var apparel = (Apparel)ThingMaker.MakeThing(def, stuffDef);

                    var hpStr = li.Attributes?["hp"]?.Value;
                    if (!hpStr.NullOrEmpty() && int.TryParse(hpStr, out var hp))
                        apparel.HitPoints = hp;

                    var qualStr = li.Attributes?["quality"]?.Value;
                    if (!qualStr.NullOrEmpty())
                    {
                        if (ParseEnum<QualityCategory>(qualStr, QualityCategory.Normal) is var qual)
                            apparel.TryGetComp<CompQuality>()?.SetQuality(qual, ArtGenerationContext.Outsider);
                    }

                    var colorStr = li.Attributes?["color"]?.Value;
                    if (!colorStr.NullOrEmpty())
                    {
                        var parts = colorStr.Split(',');
                        if (parts.Length >= 3)
                        {
                            var ci = System.Globalization.CultureInfo.InvariantCulture;
                            var color = new UnityEngine.Color(
                                float.Parse(parts[0], ci), float.Parse(parts[1], ci),
                                float.Parse(parts[2], ci), parts.Length >= 4 ? float.Parse(parts[3], ci) : 1f);
                            apparel.TryGetComp<CompColorable>()?.SetColor(color);
                        }
                    }

                    pawn.apparel.Wear(apparel, dropReplacedApparel: false,
                        locked: li.Attributes?["locked"]?.Value == "true");
                }
            }
            catch (Exception ex) { Warn($"Apparel: {ex.Message}"); }
        }

        var equipNode = root.SelectSingleNode("equipment");
        if (equipNode != null && pawn.equipment != null)
        {
            try
            {
                foreach (XmlNode li in equipNode.SelectNodes("li"))
                {
                    if (!IsAvailable(li)) continue;
                    var defName = li.Attributes?["defName"]?.Value;
                    if (defName.NullOrEmpty()) continue;
                    var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                    if (def == null) { Warn($"Equipment '{defName}' not found, skipping"); continue; }

                    ThingDef stuffDef = null;
                    var stuffName = li.Attributes?["stuff"]?.Value;
                    if (!stuffName.NullOrEmpty())
                        stuffDef = DefDatabase<ThingDef>.GetNamedSilentFail(stuffName);

                    var weapon = (ThingWithComps)ThingMaker.MakeThing(def, stuffDef);

                    var hpStr = li.Attributes?["hp"]?.Value;
                    if (!hpStr.NullOrEmpty() && int.TryParse(hpStr, out var hp))
                        weapon.HitPoints = hp;

                    var qualStr = li.Attributes?["quality"]?.Value;
                    if (!qualStr.NullOrEmpty())
                        weapon.TryGetComp<CompQuality>()?.SetQuality(
                            ParseEnum<QualityCategory>(qualStr, QualityCategory.Normal), ArtGenerationContext.Outsider);

                    pawn.equipment.AddEquipment(weapon);
                }
            }
            catch (Exception ex) { Warn($"Equipment: {ex.Message}"); }
        }
    }
}
