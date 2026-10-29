using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

// The Skills column of the Bio tab: presets, and one row per skill with passion, level bar and +/-.
public partial class TabWorker_Bio_Humanlike
{
    private const int MaxSkillLevel = 20;
    private const float SkillRowHeight = 26f;
    private const float SkillRowGap = 3f;
    private const float PassionButtonSize = 30f;
    private const float SkillStepButtonSize = 30f;
    private const float PresetButtonWidth = 60f;

    private void DoSkills(Rect inRect, Pawn pawn)
    {
        DrawSkillsHeader(ref inRect, pawn);
        inRect.xMin += 4;
        inRect.yMin += 4f;

        var labelWidth = SkillUI.skillDefsInListOrderCached.Select(def => Text.CalcSize(def.LabelCap.Resolve()).x).Max() + 16f;
        using (new TextBlock(TextAnchor.MiddleLeft))
            foreach (var def in SkillUI.skillDefsInListOrderCached)
            {
                DrawSkillRow(inRect.TakeTopPart(SkillRowHeight), pawn.skills.GetSkill(def), labelWidth, pawn);
                inRect.yMin += SkillRowGap;
            }
    }

    private static void DrawSkillsHeader(ref Rect inRect, Pawn pawn)
    {
        var header = inRect.TakeTopPart(Text.LineHeight);
        Widgets.Label(header, "Skills".Translate().Colorize(ColoredText.TipSectionTitleColor));
        GUI.color = Color.white;
        if (Widgets.ButtonText(header.TakeRightPart(PresetButtonWidth), "PawnEditor.Preset".Translate()))
            Find.WindowStack.Add(new FloatMenu(SkillPresetOptions(pawn)));
        if (!ShowExpertise) return;
        var expertiseRect = header.TakeRightPart(ExpertiseButtonWidth + 4f);
        expertiseRect.xMax -= 4f;
        DrawExpertiseButton(expertiseRect, pawn);
    }

    /// <summary>Set every skill to 0 or max, plus passion presets (VSE's own when it is active).</summary>
    private static List<FloatMenuOption> SkillPresetOptions(Pawn pawn)
    {
        var skills = "Skills".Translate().ToLower();
        var options = new List<FloatMenuOption>
        {
            new("PawnEditor.SetAllTo".Translate(skills, 0), GetSetDelegate(pawn, false, 0)),
            new("PawnEditor.SetAllTo".Translate(skills, "PawnEditor.Max".Translate()), GetSetDelegate(pawn, false, MaxSkillLevel))
        };

        if (VSECompat.Active)
            VSECompat.AddPassionPresets(options, pawn);
        else
            options.AddRange(new[] { Passion.None, Passion.Minor, Passion.Major }.Select(passion =>
                new FloatMenuOption("PawnEditor.SetAllTo".Translate("PawnEditor.Passions".Translate(), passion.GetLabel()),
                    GetSetDelegate(pawn, true, (int)passion))));
        return options;
    }

    private static void DrawSkillRow(Rect rect, SkillRecord skill, float labelWidth, Pawn pawn)
    {
        Widgets.DrawHighlightIfMouseover(rect);
        TooltipHandler.TipRegion(rect, () => SkillUI.GetSkillDescription(skill), skill.def.GetHashCode() * 397945);
        Widgets.Label(rect.TakeLeftPart(labelWidth), skill.def.LabelCap);
        DrawPassionButton(rect.TakeLeftPart(PassionButtonSize), skill, pawn);
        DrawSkillBar(rect, skill);
    }

    private static void DrawPassionButton(Rect rect, SkillRecord skill, Pawn pawn)
    {
        if (VSECompat.Active)
        {
            if (Widgets.ButtonImage(rect, VSECompat.GetPassionIcon(skill.passion)))
                Find.WindowStack.Add(new FloatMenu(VSECompat.GetPassionFloatMenuOptions(skill, pawn)));
            TooltipHandler.TipRegion(rect, VSECompat.GetPassionTooltip(skill.passion));
            return;
        }

        if (Widgets.ButtonImage(rect, GetPassionIcon(skill.passion)))
            Find.WindowStack.Add(new FloatMenu(GetVanillaPassionOptions(skill)));
        TooltipHandler.TipRegion(rect, GetPassionTooltip(skill.passion));
    }

    /// <summary>Level bar with the number on top, and +/- buttons. A disabled skill shows "-" and no bar.</summary>
    private static void DrawSkillBar(Rect rect, SkillRecord skill)
    {
        var level = skill.GetLevel();
        var disabled = skill.TotallyDisabled;
        if (!disabled)
            Widgets.FillableBar(rect, Mathf.Max(0.01f, level / (float)MaxSkillLevel), SkillBarTexture(skill), TexPawnEditor.SkillBarBGTex, false);

        rect.xMin += 3;
        Widgets.Label(rect, disabled ? "-" : level.ToString());
        if (disabled) return;

        if (Widgets.ButtonImage(rect.TakeRightPart(SkillStepButtonSize).ContractedBy(5), TexButton.Plus))
            ChangeSkillLevel(skill, +1);
        if (Widgets.ButtonImage(rect.TakeRightPart(SkillStepButtonSize).ContractedBy(5), TexButton.Minus))
            ChangeSkillLevel(skill, -1);
    }

    /// <summary>Biotech aptitude tints the bar: one color for a positive aptitude, another for a negative one.</summary>
    private static Texture2D SkillBarTexture(SkillRecord skill)
    {
        if (!ModsConfig.BiotechActive || skill.Aptitude == 0) return SkillUI.SkillBarFillTex;
        return skill.Aptitude > 0 ? SkillUI.SkillBarAptitudePositiveTex : SkillUI.SkillBarAptitudeNegativeTex;
    }

    private static void ChangeSkillLevel(SkillRecord skill, int delta)
    {
        skill.Level = skill.GetLevel(false) + delta;
        PawnEditor.Notify_PointsUsed();
    }

    private static List<FloatMenuOption> GetVanillaPassionOptions(SkillRecord skill) =>
        new()
        {
            new(Passion.None.GetLabel(), () => SetVanillaPassion(skill, Passion.None), GetPassionIcon(Passion.None), Color.white),
            new(Passion.Minor.GetLabel(), () => SetVanillaPassion(skill, Passion.Minor), GetPassionIcon(Passion.Minor), Color.white),
            new(Passion.Major.GetLabel(), () => SetVanillaPassion(skill, Passion.Major), GetPassionIcon(Passion.Major), Color.white)
        };

    private static void SetVanillaPassion(SkillRecord skill, Passion passion)
    {
        skill.passion = passion;
        PawnEditor.Notify_PointsUsed();
    }

    private static Texture2D GetPassionIcon(Passion passion) => passion switch
    {
        Passion.None => TexPawnEditor.PassionEmptyTex,
        Passion.Minor => SkillUI.PassionMinorIcon,
        Passion.Major => SkillUI.PassionMajorIcon,
        _ => TexPawnEditor.PassionEmptyTex
    };

    private static string GetPassionTooltip(Passion passion) =>
        passion.GetLabel() + "\n\n" +
        "PawnEditor.PassionLearningSpeed".Translate(passion.GetLearningFactor().ToStringPercent());

    /// <summary>Sets every skill's level, or every skill's passion, to <paramref name="value"/>.</summary>
    public static Action GetSetDelegate(Pawn pawn, bool passions, int value)
    {
        return () =>
        {
            foreach (var skillRecord in pawn.skills.skills)
                if (passions)
                    skillRecord.passion = (Passion)value;
                else
                    skillRecord.Level = value;

            // Bulk passion change: reconcile VSE passion hediffs so removed passions leave no orphan
            // hediffs and added ones actually work.
            if (passions && VSECompat.Active) VSECompat.SyncPassionHediffs(pawn);

            PawnEditor.Notify_PointsUsed();
        };
    }
}
