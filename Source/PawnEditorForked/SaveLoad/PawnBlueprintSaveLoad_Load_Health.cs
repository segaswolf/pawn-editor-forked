using System;
using System.Collections.Generic;
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

        StripGeneratorHediffs(pawn, hediffsNode);

        try
        {
            // Two-pass approach: non-implants first, then implants sorted by depth.
            // RestorePart on a parent body part removes hediffs on child parts,
            // so implants must be added deepest-first (leaves before roots).
            var normalNodes = new System.Collections.Generic.List<XmlNode>();
            var implantNodes = new System.Collections.Generic.List<(XmlNode node, BodyPartRecord part, int depth)>();
            // Modules (arm blade, analyzer, mechanalyzer...) sit ON a modular prosthetic. They are NOT
            // implants, so they used to load in pass 1 — and then pass 2's RestorePart, cleaning the
            // slot for the host implant, wiped them right back off. Same family as the bionic jaw
            // duplication bug. They must go LAST, after their host part exists. See third pass below.
            var moduleNodes = new System.Collections.Generic.List<(XmlNode node, BodyPartRecord part)>();

            foreach (XmlNode li in hediffsNode.SelectNodes("li"))
            {
                if (!IsAvailable(li)) continue;
                var defName = li.Attributes?["defName"]?.Value;
                if (defName.NullOrEmpty()) continue;
                var def = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
                if (def == null) { Warn($"Hediff '{defName}' not found, skipping"); continue; }

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
                        part = ResolveSavedPart(candidates, bodyPartLabel, defName);
                    }
                }
                if (bodyPartDefName != null && part == null) continue;

                bool isImplant = li.Attributes?["isImplant"]?.Value == "true";
                bool isModule = BionicModularityCompat.IsModule(def) || ModularModulesCompat.GetModule(def) != null;

                if (isModule)
                    moduleNodes.Add((li, part)); // deferred to pass 3, after its host part exists
                else if (isImplant)
                    implantNodes.Add((li, part, GetBodyPartDepthForLoad(part)));
                else
                    normalNodes.Add(li);
            }

            // Pass 1: Add non-implant hediffs
            foreach (var li in normalNodes)
                LoadSingleHediff(pawn, li, isImplant: false, resolvedPart: null);

            // Pass 2: Add implants, deepest body parts first
            implantNodes.Sort((a, b) => b.depth.CompareTo(a.depth));
            foreach (var (li, part, _) in implantNodes)
                LoadSingleHediff(pawn, li, isImplant: true, resolvedPart: part);

            // Pass 3: Add modules LAST. Their host prosthetic now exists and its RestorePart has already
            // run, so nothing wipes them. A module isn't an implant, so RestorePart is not called for it.
            foreach (var (li, part) in moduleNodes)
                LoadSingleHediff(pawn, li, isImplant: false, resolvedPart: part);
        }
        catch (Exception ex) { Log.Error($"[Pawn Editor] LoadHediffs: {ex}"); }
    }

    /// <summary>
    /// SAFEGUARD, run after every blueprint load: compares what the file asked for against what the
    /// pawn actually ended up with, and reports both directions.
    ///
    /// We cannot anticipate every mod, every def that disappears or every framework that hooks health.
    /// What we CAN do is refuse to fail silently. A pawn that comes back subtly wrong used to look like
    /// a successful load; now the log names exactly what is missing and what appeared out of nowhere,
    /// which is what turned the "clone with a destroyed left arm" report into a five minute diagnosis.
    ///
    /// Purely informational: it never modifies the pawn, so it cannot itself break a load.
    /// </summary>
    internal static void AuditLoadedHediffs(Pawn pawn, XmlNode root)
    {
        try
        {
            var hediffsNode = root?.SelectSingleNode("hediffs");
            if (hediffsNode == null || pawn?.health?.hediffSet?.hediffs == null) return;

            var expected = new List<string>();
            foreach (XmlNode li in hediffsNode.SelectNodes("li"))
            {
                if (!IsAvailable(li)) continue; // came from a mod the player doesn't have: not a fault
                var defName = li.Attributes?["defName"]?.Value;
                if (!defName.NullOrEmpty()) expected.Add(defName);
            }

            var actual = pawn.health.hediffSet.hediffs
                .Where(h => h?.def != null).Select(h => h.def.defName).ToList();

            // Count-aware on both sides: five missing fingers must not collapse into one entry.
            var missing = new List<string>(actual);
            var missingFromPawn = expected.Where(defName => !missing.Remove(defName)).ToList();

            var leftovers = new List<string>(expected);
            var unexpected = actual.Where(defName => !leftovers.Remove(defName)).ToList();

            if (missingFromPawn.Count > 0)
                Log.Warning($"[Pawn Editor] '{pawn.LabelShortCap}': {missingFromPawn.Count} hediff(s) in the "
                            + $"blueprint did NOT end up on the pawn: {string.Join(", ", missingFromPawn)}");

            if (unexpected.Count > 0)
                Log.Warning($"[Pawn Editor] '{pawn.LabelShortCap}': {unexpected.Count} hediff(s) on the pawn "
                            + $"are NOT in the blueprint: {string.Join(", ", unexpected)}");
        }
        catch (Exception ex)
        {
            Warn($"AuditLoadedHediffs: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes the hediffs the PAWN GENERATOR invented for this body that the blueprint does not
    /// declare: age injuries, addictions, random scars. A blueprint describes a pawn completely, so
    /// anything extra is noise, and it was arriving on loaded pawns (tobacco dependence, bite scars on
    /// a pawn that had none).
    ///
    /// Deliberately NOT a blind hediffs.Clear(): genes run BEFORE this and can add hediffs of their
    /// own, and other mods' genes do it too. We only touch defs captured in the pre-load snapshot, so
    /// anything a gene added during the load is untouched by construction. Removal goes through
    /// RemoveHediff (not raw list surgery) so RimWorld runs its own cleanup, and each removal is
    /// isolated: one hediff that misbehaves on removal can't abort the rest of the load.
    /// </summary>
    private static void StripGeneratorHediffs(Pawn pawn, XmlNode hediffsNode)
    {
        if (GeneratorHediffs.NullOrEmpty()) return;

        try
        {
            // What the blueprint DOES declare, so we never remove something we are about to restore.
            var declared = new HashSet<string>();
            foreach (XmlNode li in hediffsNode.SelectNodes("li"))
            {
                var defName = li.Attributes?["defName"]?.Value;
                if (!defName.NullOrEmpty()) declared.Add(defName);
            }

            var removed = new List<string>();
            foreach (var hediff in GeneratorHediffs)
            {
                if (hediff?.def == null) continue;
                if (declared.Contains(hediff.def.defName)) continue;
                if (!pawn.health.hediffSet.hediffs.Contains(hediff)) continue;

                try
                {
                    pawn.health.RemoveHediff(hediff);
                    removed.Add(hediff.def.defName);
                }
                catch (Exception ex)
                {
                    // Never take the whole load down over one stubborn hediff from some other mod.
                    Warn($"Could not remove generated hediff '{hediff.def.defName}': {ex.Message}");
                }
            }

            if (removed.Count > 0)
                Log.Message($"[Pawn Editor] Dropped {removed.Count} hediff(s) the generator added to "
                            + $"'{pawn.LabelShortCap}' and the blueprint does not have: {string.Join(", ", removed)}");
        }
        catch (Exception ex)
        {
            Warn($"StripGeneratorHediffs: {ex.Message}");
        }
    }

    /// <summary>
    /// Picks the body part a saved hediff belongs to, honouring the SIDE that was recorded.
    ///
    /// This used to end in `?? candidates.FirstOrDefault()`, and that fallback was mutilating pawns.
    /// Real case: a blueprint with a power arm on the right shoulder also stores MissingBodyPart for
    /// the right arm, humerus, radius and fingers. Applying "right arm is missing" removes all of its
    /// children, so when the "right humerus" entry is processed there is no right humerus left, the
    /// label match fails, and the fallback happily picked the LEFT humerus. The clone came back with a
    /// destroyed healthy arm on the other side.
    ///
    /// If a side was recorded and that exact part isn't available, the honest answer is to skip the
    /// hediff and say so, never to put it somewhere else.
    /// </summary>
    private static BodyPartRecord ResolveSavedPart(List<BodyPartRecord> candidates, string bodyPartLabel, string defName)
    {
        if (candidates == null || candidates.Count == 0) return null;
        if (bodyPartLabel.NullOrEmpty()) return candidates.FirstOrDefault();

        var match = candidates.FirstOrDefault(p => p.Label == bodyPartLabel);
        if (match == null)
            Warn($"'{defName}': saved body part '{bodyPartLabel}' is not available on this pawn "
                 + "(already replaced or missing), skipping it rather than using a different part");

        return match;
    }

    private static int GetBodyPartDepthForLoad(BodyPartRecord part)
    {
        int depth = 0;
        var current = part;
        while (current?.parent != null) { depth++; current = current.parent; }
        return depth;
    }

    private static void LoadSingleHediff(Pawn pawn, XmlNode li, bool isImplant, BodyPartRecord resolvedPart)
    {
        var defName = li.Attributes?["defName"]?.Value;
        if (defName.NullOrEmpty()) return;
        var def = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
        if (def == null) return;

        // Re-resolve part if not pre-resolved (normal hediffs)
        BodyPartRecord part = resolvedPart;
        if (part == null)
        {
            var bodyPartDefName = li.Attributes?["bodyPart"]?.Value;
            var bodyPartLabel   = li.Attributes?["bodyPartLabel"]?.Value;
            if (!bodyPartDefName.NullOrEmpty())
            {
                var bpDef = DefDatabase<BodyPartDef>.GetNamedSilentFail(bodyPartDefName);
                if (bpDef != null)
                {
                    var candidates = pawn.health.hediffSet.GetNotMissingParts()
                        .Where(p => p.def == bpDef).ToList();
                    part = ResolveSavedPart(candidates, bodyPartLabel, defName);
                }
            }
            if (bodyPartDefName != null && part == null) return;
        }

        try
        {
            if (isImplant && part != null)
            {
                // Only RestorePart if no implant already on this exact part
                bool alreadyHas = pawn.health.hediffSet.hediffs.Any(h =>
                    h.Part == part && (h is Hediff_AddedPart || h is Hediff_Implant));
                if (!alreadyHas)
                    try { pawn.health.RestorePart(part, null, checkStateChange: false); } catch { }
            }

            var hediff = HediffMaker.MakeHediff(def, pawn, part);
            var severityStr = li.Attributes?["severity"]?.Value;
            if (!severityStr.NullOrEmpty() && float.TryParse(severityStr,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var sev))
                hediff.Severity = sev;

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
                // Clear existing apparel to prevent loadID conflicts
                pawn.apparel.DestroyAll();

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
                // Clear existing equipment to prevent loadID conflicts
                pawn.equipment.DestroyAllEquipment();

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
