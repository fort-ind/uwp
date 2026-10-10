using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;
using Windows.UI.Input.Inking;

namespace Fort.ind_UWP
{
    public sealed class InkHistory
    {
        private readonly InkStrokeContainer _container;

        private readonly Dictionary<InkStroke, Slot> _slots = new Dictionary<InkStroke, Slot>();

        private readonly Stack<Operation> _undo = new Stack<Operation>();

        private readonly Stack<Operation> _redo = new Stack<Operation>();

        public InkHistory(InkStrokeContainer container)
        {
            _container = container;
        }

        public event EventHandler Changed;

        public bool CanUndo
        {
            get { return _undo.Count > 0; }
        }

        public bool CanRedo
        {
            get { return _redo.Count > 0; }
        }

        public void Reset()
        {
            _undo.Clear();
            _redo.Clear();
            _slots.Clear();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void RecordAdded(IEnumerable<InkStroke> strokes)
        {
            Push(new Operation(OperationKind.Add, SlotsFor(strokes), default(Point)));
        }

        public void RecordRemoved(IEnumerable<InkStroke> strokes)
        {
            Push(new Operation(OperationKind.Remove, SlotsFor(strokes), default(Point)));
        }

        public void RecordMoved(IEnumerable<InkStroke> strokes, Point offset)
        {
            if (offset.X == 0 && offset.Y == 0) return;
            Push(new Operation(OperationKind.Move, SlotsFor(strokes), offset));
        }

        public void Undo()
        {
            if (_undo.Count == 0) return;

            var operation = _undo.Pop();
            Apply(operation, true);
            _redo.Push(operation);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Redo()
        {
            if (_redo.Count == 0) return;

            var operation = _redo.Pop();
            Apply(operation, false);
            _undo.Push(operation);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void Push(Operation operation)
        {
            if (operation.Slots.Count == 0) return;

            _undo.Push(operation);
            _redo.Clear();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private List<Slot> SlotsFor(IEnumerable<InkStroke> strokes)
        {
            var slots = new List<Slot>();
            foreach (var stroke in strokes)
            {
                Slot slot;
                if (!_slots.TryGetValue(stroke, out slot))
                {
                    slot = new Slot(stroke);
                    _slots[stroke] = slot;
                }

                slots.Add(slot);
            }

            return slots;
        }

        private void Apply(Operation operation, bool undo)
        {
            switch (operation.Kind)
            {
                case OperationKind.Add:
                    if (undo) Delete(operation.Slots);
                    else Restore(operation.Slots);
                    break;
                case OperationKind.Remove:
                    if (undo) Restore(operation.Slots);
                    else Delete(operation.Slots);
                    break;
                case OperationKind.Move:
                    var offset = undo ? new Point(-operation.Offset.X, -operation.Offset.Y) : operation.Offset;
                    Select(operation.Slots);
                    _container.MoveSelected(offset);
                    Select(null);
                    break;
            }
        }

        private void Delete(List<Slot> slots)
        {
            Select(slots);
            _container.DeleteSelected();
        }

        private void Restore(List<Slot> slots)
        {
            Select(null);
            foreach (var slot in slots)
            {
                var clone = slot.Stroke.Clone();
                _container.AddStroke(clone);
                _slots.Remove(slot.Stroke);
                slot.Stroke = clone;
                _slots[clone] = slot;
            }
        }

        private void Select(List<Slot> slots)
        {
            var wanted = slots == null ? new HashSet<InkStroke>() : new HashSet<InkStroke>(slots.Select(slot => slot.Stroke));
            foreach (var stroke in _container.GetStrokes())
            {
                stroke.Selected = wanted.Contains(stroke);
            }
        }

        private enum OperationKind
        {
            Add,
            Remove,
            Move
        }

        private sealed class Slot
        {
            public Slot(InkStroke stroke)
            {
                Stroke = stroke;
            }

            public InkStroke Stroke { get; set; }
        }

        private sealed class Operation
        {
            public Operation(OperationKind kind, List<Slot> slots, Point offset)
            {
                Kind = kind;
                Slots = slots;
                Offset = offset;
            }

            public OperationKind Kind { get; private set; }

            public List<Slot> Slots { get; private set; }

            public Point Offset { get; private set; }
        }
    }
}
