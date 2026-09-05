using System;
using System.Collections.Generic;
using NWorld.Map.Models;
using NWorld.MapServices.MapRenderComponents.StandardRenderer.RenderingFunctions;

namespace NWorld.MapServices.Editing
{
    /// <summary>
    /// The writing on one map, and everything anyone does to it: put a label down, pick one up,
    /// drag it somewhere else, change what it says, take it away again.
    /// <para>
    /// Here rather than in the app that shows it, and here rather than in <c>NWorld.Map</c>,
    /// for the two halves of the same reason. Every app that lets somebody write on a map wants
    /// exactly these operations and would otherwise write them again, slightly differently; and
    /// none of it belongs in the map library, which knows what a label <em>is</em> and draws
    /// what it is handed, but has no business deciding that clicking one picks it up. The
    /// control reports gestures in map pixels, and this turns gestures into labels.
    /// </para>
    /// <para>
    /// The hit testing comes from <see cref="RenderMapLabel"/> rather than being worked out
    /// again here: what a click lands on has to be what the eye sees, and the only thing that
    /// knows how wide a label is drawn is the thing that draws it.
    /// </para>
    /// <para>
    /// <see cref="Labels"/> is a fresh array after every change and the working list is never
    /// handed out, because what goes on screen is read on the render thread and must not move
    /// under it. Labels are counted in dozens, so a whole new array per edit costs nothing.
    /// </para>
    /// </summary>
    public sealed class MapLabelEditor
    {
        private readonly List<MapLabel> _labels = [];

        /// <summary>
        /// Where the label being dragged was taken hold of, measured from its own middle, or
        /// null when nothing is being dragged.
        /// <para>
        /// Kept so that a label picked up by its left-hand end stays picked up by its left-hand
        /// end. Without it every drag would snap the label's middle to the cursor, which is a
        /// jump at the start of every gesture and leaves the label somewhere nobody pointed at.
        /// </para>
        /// </summary>
        private MapPixel? _grip;

        /// <summary>
        /// The label being dragged. Kept apart from <see cref="Picked"/> because a drag can be
        /// cut short at any moment and what is being edited should not be.
        /// </summary>
        private MapLabel? _dragged;

        /// <summary>
        /// The writing as it stands, ready to be published to <c>MapView.Labels</c>. A new
        /// array after every change, so binding to it repaints, and the one handed out before
        /// it stays safe for a frame that is still being drawn.
        /// </summary>
        public IReadOnlyList<MapLabel> Labels { get; private set; } = [];

        /// <summary>
        /// The label being edited, or null when none is. What <see cref="Rewrite"/> and
        /// <see cref="Remove"/> act on.
        /// </summary>
        public MapLabel? Picked { get; private set; }

        /// <summary>Whether a drag is holding a label.</summary>
        public bool IsDragging => _dragged is not null;

        /// <summary>How many labels there are.</summary>
        public int Count => _labels.Count;

        /// <summary>
        /// Puts a new label down with its middle at <paramref name="at"/> and picks it, since
        /// what somebody has just written is the thing they are most likely to want to change.
        /// Refuses a label with nothing written on it: there would be nothing to see and
        /// nothing to click on afterwards.
        /// </summary>
        /// <returns>The label, or null if there was nothing to write.</returns>
        public MapLabel? Write(MapPixel at, string? text, uint background, uint foreground)
        {
            var words = text?.Trim();

            if (string.IsNullOrEmpty(words))
                return null;

            var label = new MapLabel
            {
                Text = words,
                Anchor = at,
                Background = background,
                Foreground = foreground,
            };

            _labels.Add(label);
            Picked = label;
            Publish();

            return label;
        }

        /// <summary>
        /// Picks the label at <paramref name="point"/>, or unpicks everything if there is none
        /// there.
        /// </summary>
        /// <returns>What is now picked, which may be null.</returns>
        public MapLabel? Pick(MapPixel point)
        {
            Picked = RenderMapLabel.At(_labels, point);
            return Picked;
        }

        /// <summary>Unpicks whatever is picked.</summary>
        public void Unpick() => Picked = null;

        /// <summary>
        /// Changes what the picked label says, and the colours it says it in, leaving it
        /// exactly where it is.
        /// <para>
        /// A replacement and not an edit, because a <see cref="MapLabel"/> published to the
        /// render thread is never written to again. The picked label follows the replacement,
        /// so a run of keystrokes edits the one label rather than leaving a trail of them.
        /// </para>
        /// </summary>
        /// <returns>Whether anything changed.</returns>
        public bool Rewrite(string? text, uint background, uint foreground)
        {
            if (Picked is not { } label)
                return false;

            var words = text?.Trim();

            // An empty box is somebody midway through retyping the label, not somebody asking
            // for a blank one. The label keeps what it says until there is something to say.
            if (string.IsNullOrEmpty(words))
                return false;

            var replacement = label with
            {
                Text = words,
                Background = background,
                Foreground = foreground,
            };

            if (replacement == label)
                return false;

            Picked = replacement;

            return Replace(label, replacement);
        }

        /// <summary>Takes the picked label away. Nothing is picked afterwards.</summary>
        /// <returns>The label that was removed, or null if none was picked.</returns>
        public MapLabel? Remove()
        {
            if (Picked is not { } label)
                return null;

            _labels.Remove(label);
            Picked = null;

            // A drag still holding the label that has just gone would put it back on the next
            // move.
            if (ReferenceEquals(_dragged, label))
                Drop();

            Publish();

            return label;
        }

        /// <summary>
        /// Takes hold of whatever label is at <paramref name="from"/>, ready to be moved, and
        /// picks it. A grab that finds nothing holds nothing and every move after it does
        /// nothing, which is the right answer for a drag begun on open ground.
        /// </summary>
        /// <returns>Whether a label was taken hold of.</returns>
        public bool Grab(MapPixel from)
        {
            _dragged = RenderMapLabel.At(_labels, from);

            if (_dragged is null)
            {
                _grip = null;
                return false;
            }

            Picked = _dragged;
            _grip = new MapPixel(from.X - _dragged.Anchor.X, from.Y - _dragged.Anchor.Y);

            return true;
        }

        /// <summary>
        /// Moves the label being dragged, keeping the hold on it that it was grabbed with. Does
        /// nothing if the drag grabbed nothing.
        /// </summary>
        /// <returns>Whether the label moved.</returns>
        public bool DragTo(MapPixel at)
        {
            if (_dragged is not { } label || _grip is not { } grip)
                return false;

            var moved = label with { Anchor = new MapPixel(at.X - grip.X, at.Y - grip.Y) };

            if (moved.Anchor == label.Anchor)
                return false;

            _dragged = moved;

            if (ReferenceEquals(Picked, label))
                Picked = moved;

            return Replace(label, moved);
        }

        /// <summary>
        /// Lets go. Safe to call whether or not anything was being held, which is what lets a
        /// caller answer every finished drag the same way.
        /// </summary>
        public void Drop()
        {
            _dragged = null;
            _grip = null;
        }

        /// <summary>Puts a saved set of labels in place of whatever is there.</summary>
        public void Load(IEnumerable<MapLabel>? labels)
        {
            _labels.Clear();
            Picked = null;
            Drop();

            if (labels is not null)
            {
                foreach (var label in labels)
                {
                    if (label is not null && !string.IsNullOrWhiteSpace(label.Text))
                        _labels.Add(label);
                }
            }

            Publish();
        }

        /// <summary>Rubs out everything. What a newly opened map starts from.</summary>
        public void Clear() => Load(null);

        /// <summary>
        /// Swaps one label for another in place, so the order they were written in is the order
        /// they stay in -- which is the order they are drawn in, and so decides what covers what
        /// and therefore what a click picks.
        /// </summary>
        private bool Replace(MapLabel label, MapLabel replacement)
        {
            var index = _labels.IndexOf(label);

            if (index < 0)
                return false;

            _labels[index] = replacement;
            Publish();

            return true;
        }

        private void Publish() => Labels = _labels.ToArray();
    }
}
