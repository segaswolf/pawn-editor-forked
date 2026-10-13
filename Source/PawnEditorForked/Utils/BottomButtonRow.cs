using UnityEngine;

namespace PawnEditor;

/// <summary>The four middle buttons of the editor's bottom row: equal slots, centered as one group.</summary>
/// <remarks>
/// <para>
/// Pure layout with no game types, so it is unit-tested. One group under one rule replaced mixed
/// anchoring that let Save and Load collide with Randomize.
/// </para>
/// </remarks>
public readonly struct BottomButtonRow
{
    public const float MinButtonWidth = 60f;

    public readonly Rect Delete;
    public readonly Rect Randomize;
    public readonly Rect Save;
    public readonly Rect Load;

    private BottomButtonRow(Rect delete, Rect randomize, Rect save, Rect load)
    {
        Delete = delete;
        Randomize = randomize;
        Save = save;
        Load = load;
    }

    /// <summary>Lays the four slots out inside <paramref name="area"/>, each at most <paramref name="buttonSize"/>.</summary>
    public static BottomButtonRow Within(Rect area, Vector2 buttonSize)
    {
        var spacing = Spacing(area.width);
        area.xMin += spacing;
        area.xMax -= spacing;

        var slotWidth = Mathf.Clamp((area.width - spacing * 3f) / 4f, MinButtonWidth, buttonSize.x);
        var groupWidth = slotWidth * 4f + spacing * 3f;
        var x = area.x + Mathf.Max(0f, (area.width - groupWidth) / 2f);
        var y = area.y + (area.height - buttonSize.y) / 2f;

        Rect Slot(int index) => new(x + index * (slotWidth + spacing), y, slotWidth, buttonSize.y);
        return new BottomButtonRow(Slot(0), Slot(1), Slot(2), Slot(3));
    }

    /// <summary>Gap between slots, also kept at both ends so the group never touches Close or Teleport.</summary>
    public static float Spacing(float areaWidth) => Mathf.Clamp(areaWidth / 40f, 6f, 16f);
}
