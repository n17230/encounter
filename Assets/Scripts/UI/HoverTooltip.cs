using System;
using UnityEngine;
using UnityEngine.UIElements;

// Cursor-following hover tooltip for UI Toolkit panels - there's no
// equivalent of IMGUI's automatic GUI.tooltip tracking, so this is the
// shared replacement mechanism every migrated panel with hoverable rows
// (Skills, and Gear once it migrates) attaches to its own document. Plain
// C# helper, not a MonoBehaviour - one instance per panel document, created
// by that panel's controller and given its own root to add the floating
// tooltip element into.
public class HoverTooltip
{
    private readonly VisualElement tooltipElement;
    private readonly Label label;

    public HoverTooltip(VisualElement root)
    {
        tooltipElement = new VisualElement();
        tooltipElement.AddToClassList("hover-tooltip");
        tooltipElement.pickingMode = PickingMode.Ignore;
        tooltipElement.style.display = DisplayStyle.None;

        label = new Label();
        label.AddToClassList("hover-tooltip__text");
        tooltipElement.Add(label);

        root.Add(tooltipElement);
    }

    // getText is invoked fresh on every hover (not cached), so it always
    // reflects the target's current state (e.g. an ability slot whose
    // binding just changed).
    public void Attach(VisualElement target, Func<string> getText)
    {
        target.RegisterCallback<PointerEnterEvent>(_ =>
        {
            label.text = getText();
            tooltipElement.style.display = DisplayStyle.Flex;
            tooltipElement.BringToFront();
        });
        target.RegisterCallback<PointerLeaveEvent>(_ => tooltipElement.style.display = DisplayStyle.None);
        target.RegisterCallback<PointerMoveEvent>(evt =>
        {
            tooltipElement.style.left = evt.position.x + 16f;
            tooltipElement.style.top = evt.position.y + 16f;
        });
    }
}
