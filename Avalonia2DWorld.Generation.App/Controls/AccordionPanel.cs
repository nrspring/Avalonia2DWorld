using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Avalonia2DWorld.Generation.App.Controls;

/// <summary>
/// A stack of <see cref="Expander"/> panels of which at most one is open at a time: opening
/// one closes the rest and raises it to the top of the stack, and closing it drops it back
/// where it was declared.
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

    /// <summary>
    /// The children in the order they were declared, so that the one raised to the top can be
    /// put back. Captured on attach, before anything has been moved.
    /// </summary>
    private Control[]? _order;

    static AccordionPanel()
    {
        // On the property rather than on each child: panels added later -- and the header of
        // one that has not been realised yet -- are covered without anything having to
        // subscribe. The ancestor check below is what keeps it to expanders that are actually
        // in an accordion.
        Expander.IsExpandedProperty.Changed.AddClassHandler<Expander>(OnExpandedChanged);
    }

    /// <summary>
    /// Records the declared order and honours whichever panel was declared open.
    /// <para>
    /// A panel opened in the markup rather than by a click never reaches the handler below:
    /// its expander is given its value before the card it sits in is added to this stack, so
    /// there is no accordion above it yet to hear about it. Attaching is the first moment the
    /// stack knows both things, which makes it the place to catch up.
    /// </para>
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _order ??= Children.ToArray();

        if (this.GetVisualDescendants().OfType<Expander>().FirstOrDefault(x => x.IsExpanded) is { } open)
            Rearrange(this, open);
    }

    private static void OnExpandedChanged(Expander expander, AvaloniaPropertyChangedEventArgs args)
    {
        if (expander.FindAncestorOfType<AccordionPanel>() is not { _closing: false } panel)
            return;

        // The open one has been closed, by this click or by nothing else being open: the
        // stack goes back to reading in the order it was written.
        if (args.NewValue is not true)
        {
            Rearrange(panel, null);
            return;
        }

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

        Rearrange(panel, expander);
    }

    /// <summary>
    /// Puts the stack back in its declared order and, if a panel is open, lifts that one to
    /// the top and scrolls to it.
    /// <para>
    /// Deferred rather than done here because the click that opened the panel is still being
    /// handled by a button inside the card about to be moved, and because the panel being
    /// opened has no height yet while the ones being closed still have theirs -- measuring
    /// anything now would be measuring the stack as it is about to stop being.
    /// </para>
    /// </summary>
    private static void Rearrange(AccordionPanel panel, Expander? open)
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                panel.Restore();

                // Re-read rather than trust what was open when this was queued: clicks can
                // land faster than the queue drains, and the last one is the one that counts.
                if (open is not { IsExpanded: true })
                    return;

                if (CardOf(panel, open) is { } card)
                {
                    var from = panel.Children.IndexOf(card);
                    if (from > 0)
                        panel.Children.Move(from, 0);
                }

                Reveal(panel, open);
            },
            DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Returns the stack's own child that the given expander sits inside -- the card, not the
    /// expander, since it is the card that carries the look and the card that moves.
    /// </summary>
    private static Control? CardOf(AccordionPanel panel, Expander expander)
    {
        Visual? child = expander;

        while (child is not null && !ReferenceEquals(child.GetVisualParent(), panel))
            child = child.GetVisualParent();

        return child as Control;
    }

    /// <summary>
    /// Moves the children back into the order they were declared in, leaving anything not in
    /// that record after them.
    /// </summary>
    private void Restore()
    {
        if (_order is not { } order)
            return;

        var target = 0;

        foreach (var child in order)
        {
            var current = Children.IndexOf(child);

            if (current < 0)
                continue;

            if (current != target)
                Children.Move(current, target);

            target++;
        }
    }

    /// <summary>
    /// Scrolls the panel just opened to the top of whatever is scrolling the stack, if
    /// anything is.
    /// <para>
    /// Because a panel can be taller than the window, and because the raise above only settles
    /// where the card sits in the stack and not where the stack is scrolled to. Scrolling to
    /// the top of the panel that was just asked for shows as much of it as there is room for,
    /// and it is the top that matters: the header says which panel this is.
    /// </para>
    /// <para>
    /// Deferred again, past the layout pass the move it follows has just invalidated -- the
    /// card's new position is not in its bounds yet.
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
