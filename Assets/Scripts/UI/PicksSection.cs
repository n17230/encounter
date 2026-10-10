using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// One icon in a player's picks row - the panel controller that owns the
// section decides what each one shows (an ability or an item icon), says in
// its tooltip, and does on click (null = tooltip only, e.g. another
// player's picks).
public struct PicksIcon
{
    public Sprite Sprite;
    public string Label;        // optional top-left tag (the equipment slot name); null = none
    public Func<string> Tooltip;
    public Action OnClick;
}

public struct PicksRowDisplay
{
    public string Name;
    public bool IsLocal;
    public List<PicksIcon> Icons;
}

// The "every player's picks" block shared by the Skills and Equipment
// panels: per player a name and a wrapping left-to-right row of icons.
// Plain C# helper owned by a panel controller, like CategoryTabBar - given
// the container to fill, the panel's HoverTooltip, and which icon size
// class to use (Theme.uss: picks-icon--ability / picks-icon--item).
public class PicksSection
{
    private readonly VisualElement container;
    private readonly HoverTooltip tooltip;
    private readonly string iconSizeClass;

    public PicksSection(VisualElement container, HoverTooltip tooltip, string iconSizeClass)
    {
        this.container = container;
        this.tooltip = tooltip;
        this.iconSizeClass = iconSizeClass;
    }

    public void Render(IReadOnlyList<PicksRowDisplay> rows)
    {
        container.Clear();
        foreach (PicksRowDisplay row in rows)
        {
            VisualElement rowElement = new VisualElement();
            rowElement.AddToClassList("picks-row");
            if (row.IsLocal) rowElement.AddToClassList("picks-row--local");

            Label name = new Label(row.Name);
            name.AddToClassList("picks-row__name");
            rowElement.Add(name);

            VisualElement icons = new VisualElement();
            icons.AddToClassList("picks-row__icons");
            if (row.Icons != null)
            {
                foreach (PicksIcon icon in row.Icons) icons.Add(BuildIcon(icon));
            }
            rowElement.Add(icons);

            container.Add(rowElement);
        }
    }

    // A Button only when there's something to click - a plain element
    // otherwise, so another player's icons don't light up as if they were.
    private VisualElement BuildIcon(PicksIcon icon)
    {
        VisualElement box = icon.OnClick != null ? new Button(icon.OnClick) : new VisualElement();
        box.AddToClassList("picks-icon");
        box.AddToClassList(iconSizeClass);
        if (icon.OnClick != null) box.AddToClassList("picks-icon--clickable");

        Image image = new Image { sprite = icon.Sprite, scaleMode = ScaleMode.ScaleToFit };
        image.AddToClassList("picks-icon__image");
        box.Add(image);

        if (!string.IsNullOrEmpty(icon.Label))
        {
            Label tag = new Label(icon.Label);
            tag.AddToClassList("picks-icon__label");
            box.Add(tag);
        }

        if (icon.Tooltip != null) tooltip.Attach(box, icon.Tooltip);
        return box;
    }
}
