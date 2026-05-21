using System;
using System.Linq;
using System.Xml;
using RimWorld;
using Verse;

namespace PawnEditor;

/// <summary>
/// Partial — Save: Health section.
/// Hediffs (including implants/prosthetics), Abilities, Apparel, Equipment.
/// </summary>
public static partial class PawnBlueprintSaveLoad
{
    // ── Save: Hediffs ──

    private static void WriteHediffs(XmlWriter w, Pawn pawn)
    {
        if (pawn.health?.hediffSet == null) return;
        w.WriteStartElement("hediffs");
        foreach (var hediff in pawn.health.hediffSet.hediffs)
        {
            if (hediff?.def == null) continue;
            if (!hediff.def.duplicationAllowed) continue;

            w.WriteStartElement("li");
            w.WriteAttributeString("defName",  hediff.def.defName);
            w.WriteAttributeString("severity", hediff.Severity.ToString("F3"));
            if (hediff.Part != null)
            {
                w.WriteAttributeString("bodyPart", hediff.Part.def.defName);
                if (!hediff.Part.Label.NullOrEmpty())
                    w.WriteAttributeString("bodyPartLabel", hediff.Part.Label);
            }
            if (hediff.IsPermanent()) w.WriteAttributeString("isPermanent", "true");
            if (hediff.ageTicks > 0)  w.WriteAttributeString("ageTicks", hediff.ageTicks.ToString());
            // Mark implants/prosthetics so the load side can call RestorePart before adding
            if ((hediff is Hediff_AddedPart || hediff is Hediff_Implant) && !hediff.def.organicAddedBodypart)
                w.WriteAttributeString("isImplant", "true");
            WriteSourceMod(w, hediff.def);
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    // ── Save: Abilities ──

    private static void WriteAbilities(XmlWriter w, Pawn pawn)
    {
        if (pawn.abilities?.abilities == null) return;
        w.WriteStartElement("abilities");
        foreach (var ability in pawn.abilities.abilities)
        {
            if (ability?.def == null) continue;
            w.WriteStartElement("li");
            w.WriteAttributeString("defName", ability.def.defName);
            WriteSourceMod(w, ability.def);
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    // ── Save: Apparel & Equipment ──

    private static void WriteApparel(XmlWriter w, Pawn pawn)
    {
        if (pawn.apparel?.WornApparel != null && pawn.apparel.WornApparel.Count > 0)
        {
            w.WriteStartElement("apparel");
            foreach (var worn in pawn.apparel.WornApparel)
            {
                if (worn?.def == null) continue;
                w.WriteStartElement("li");
                w.WriteAttributeString("defName", worn.def.defName);
                WriteSourceMod(w, worn.def);
                if (worn.Stuff != null) w.WriteAttributeString("stuff", worn.Stuff.defName);
                w.WriteAttributeString("hp",    worn.HitPoints.ToString());
                w.WriteAttributeString("maxHp", worn.MaxHitPoints.ToString());
                if (worn.TryGetQuality(out var quality))
                    w.WriteAttributeString("quality", quality.ToString());
                var colorComp = worn.TryGetComp<CompColorable>();
                if (colorComp != null && colorComp.Active)
                {
                    var c = colorComp.Color;
                    w.WriteAttributeString("color", $"{c.r:F3},{c.g:F3},{c.b:F3},{c.a:F3}");
                }
                if (pawn.apparel.IsLocked(worn)) w.WriteAttributeString("locked", "true");
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }

        if (pawn.equipment?.AllEquipmentListForReading != null && pawn.equipment.AllEquipmentListForReading.Count > 0)
        {
            w.WriteStartElement("equipment");
            foreach (var equip in pawn.equipment.AllEquipmentListForReading)
            {
                if (equip?.def == null) continue;
                w.WriteStartElement("li");
                w.WriteAttributeString("defName", equip.def.defName);
                WriteSourceMod(w, equip.def);
                if (equip.Stuff != null) w.WriteAttributeString("stuff", equip.Stuff.defName);
                w.WriteAttributeString("hp",    equip.HitPoints.ToString());
                w.WriteAttributeString("maxHp", equip.MaxHitPoints.ToString());
                if (equip.TryGetQuality(out var quality))
                    w.WriteAttributeString("quality", quality.ToString());
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }
    }
}
