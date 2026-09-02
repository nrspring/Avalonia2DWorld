using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace NWorld.Generation.App.Controls;

/// <summary>
/// A stack of <see cref="Expander"/> panels of which at most one is open at a time: opening
/// one closes the rest.
/// <para>
/// Here rather than in the view models because it is a fact about the stack and not about any
/// panel in it. A view model would need every panel to know about every other one, and adding
/// the fifth would mean editing the other four; this way a new panel is a new child and
/// nothing else.
/// </para>
/// <para>
/// Closing the open one is allowed. An accordion that insists on always having something open
/// is a tab strip wearing a disguise, and the map underneath is worth more than any of these
/// panels.
/// </para>
/// </summary>
public class AccordionPanel : StackPanel
{
    /// <summary>
    /// Set while this panel is closing its own children, so the changes it makes do not come
    /// back through the handler as changes to react to.
    /// </summary>
    private bool _closing;

    static AccordionPanel()
    {
        // On the property rather than on each child: panels added later -- and the header of
        // one that has not been realised yet -- are covered without anything having to
        // subscribe. The ancestor check below is what keeps it to expanders that are actually
        // in an accordion.
        Expander.IsExpandedProperty.Changed.AddClassHandler<Expander>(OnExpandedChanged);
    }

    private static void OnExpandedChanged(Expander expander, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is not true)
            return;

        if (expander.FindAncestorOfType<AccordionPanel>() is not { _closing: false } panel)
            return;

        panel._closing = true;
        try
        {
            foreach (var other in panel.GetVisualDescendants().OfType<Expander>())
            {
                if (!ReferenceEquals(other, expander))
                    other.IsExpanded = false;
            }
        }
        finally
        {
            panel._closing = false;
        }

        Reveal(panel, expander);
    }

    /// <summary>
    /// Scrolls the panel just opened to the top of whatever is scrolling the stack, if
    /// anything is.
    /// <para>
    /// Because a panel can be taller than the window. Opening the last one in the stack put
    /// its header on screen and left its buttons below the bottom edge -- reachable, since the
    /// stack scrolls, but only by someone who thought to look. Scrolling to the top of the
    /// panel that was just asked for shows as much of it as there is room for, and it is the
    /// top that matters: the header says which panel this is.
    /// </para>
    /// <para>
    /// Deferred to after the layout pass, because the panel being opened has no height yet and
    /// the ones being closed still have theirs -- scrolling now would be scrolling to where
    /// this panel is about to stop being.
    /// </para>
    /// </summary>
    private static void Reveal(AccordionPanel panel, Expander expander)
    {
        if (panel.FindAncestorOfType<ScrollViewer>() is not { } scroller)
            return;

        Dispatcher.UIThread.Post(
            () =>
            {
                // Where the expander's top edge sits in the scrolled content: its offset from
                // the top of the viewport, plus however far the viewport has already been
                // scrolled.
                if (expander.TranslatePoint(default, scroller) is not { } corner)
                    return;

                var furthest = Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height);
                var wanted = Math.Clamp(scroller.Offset.Y + corner.Y, 0, furthest);

                scroller.Offset = new Vector(scroller.Offset.X, wanted);
            },
            DispatcherPriority.Loaded);
    }
}
