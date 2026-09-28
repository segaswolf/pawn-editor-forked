using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

// Which defs a pawn is allowed to pick, by source mod and by genes or developmental stage, plus the
// per-tab def lists the randomizer draws from.
public partial class Dialog_AppearanceEditor
{
    private bool MatchesSource(Def def) => sourceFilter == null || def.modContentPack == sourceFilter;

    private bool IsAllowed(HeadTypeDef def, Pawn p)
    {
        if (ignoreXenotype) return true;
        if (ModsConfig.BiotechActive && !def.requiredGenes.NullOrEmpty())
        {
            if (p.genes == null)
            {
                return false;
            }

            foreach (GeneDef requiredGene in def.requiredGenes)
            {
                if (!pawn.genes.HasActiveGene(requiredGene))
                {
                    return false;
                }
            }
        }

        if (def.gender != 0)
        {
            return def.gender == p.gender;
        }

        return def.randomChosen;
    }

    private bool IsAllowed(BodyTypeDef def, Pawn p)
    {
        if (ignoreXenotype) return true;
        if (ModsConfig.BiotechActive && pawn.DevelopmentalStage.Juvenile())
        {
            return def == BodyTypeDefOf.Baby || def == BodyTypeDefOf.Child;
        }

        return true;
    }

    /// <summary>
    /// Every def the Source menu should consider for the current tab, before the Source filter. Style
    /// tabs ask their selected picker, so the menu always matches the grid on screen (it used to list
    /// hair mods while the Beard sub-tab was open).
    /// </summary>
    private IEnumerable<Def> SourceCandidatesForCurrentTab() =>
        SelectedStylePicker()?.Candidates() ?? mainTab switch
        {
            MainTab.Xenotype => CosmeticGeneDiscovery.GroupGenes.SelectMany(defs => defs.Cast<Def>()),
            MainTab.FacialAnimation => FacialAnimCompat.GetAllOptionDefs(pawn),
            _ => Enumerable.Empty<Def>()
        };
}
