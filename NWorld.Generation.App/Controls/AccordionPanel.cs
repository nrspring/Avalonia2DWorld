using System.Linq;
using Avalonia;
using Avalonia.Controls;
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
    }
}
