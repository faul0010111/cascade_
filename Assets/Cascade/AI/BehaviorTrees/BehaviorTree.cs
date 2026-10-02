using System;
using System.Collections.Generic;

namespace Cascade.AI.BehaviorTrees
{
    public enum BtStatus { Running, Success, Failure }

    /// <summary>
    /// Minimal behavior tree used ONLY to execute a single action (section 4 of ARCHITECTURE.md).
    /// Trees are small and never make strategic choices.
    /// </summary>
    public abstract class BtNode<T>
    {
        public string Name { get; protected set; }
        public BtStatus LastStatus { get; private set; }

        public BtStatus Tick(T context, float dt)
        {
            LastStatus = OnTick(context, dt);
            return LastStatus;
        }

        protected abstract BtStatus OnTick(T context, float dt);
        public virtual void Reset() { }
        public virtual string Describe() => Name + " [" + LastStatus + "]";
    }

    /// <summary>Runs children in order; fails on the first failure.</summary>
    public sealed class Sequence<T> : BtNode<T>
    {
        private readonly BtNode<T>[] _children;
        private int _index;

        public Sequence(string name, params BtNode<T>[] children) { Name = name; _children = children; }

        protected override BtStatus OnTick(T context, float dt)
        {
            while (_index < _children.Length)
            {
                var status = _children[_index].Tick(context, dt);
                if (status == BtStatus.Running) return BtStatus.Running;
                if (status == BtStatus.Failure) { Reset(); return BtStatus.Failure; }
                _index++;
            }
            Reset();
            return BtStatus.Success;
        }

        public override void Reset()
        {
            _index = 0;
            foreach (var c in _children) c.Reset();
        }

        public override string Describe()
        {
            var cur = _index < _children.Length ? _children[_index].Describe() : "-";
            return Name + " (" + (_index + 1) + "/" + _children.Length + ") > " + cur;
        }
    }

    /// <summary>Runs children in order until one succeeds.</summary>
    public sealed class Selector<T> : BtNode<T>
    {
        private readonly BtNode<T>[] _children;
        private int _index;

        public Selector(string name, params BtNode<T>[] children) { Name = name; _children = children; }

        protected override BtStatus OnTick(T context, float dt)
        {
            while (_index < _children.Length)
            {
                var status = _children[_index].Tick(context, dt);
                if (status == BtStatus.Running) return BtStatus.Running;
                if (status == BtStatus.Success) { Reset(); return BtStatus.Success; }
                _index++;
            }
            Reset();
            return BtStatus.Failure;
        }

        public override void Reset()
        {
            _index = 0;
            foreach (var c in _children) c.Reset();
        }
    }

    public sealed class Condition<T> : BtNode<T>
    {
        private readonly Func<T, bool> _predicate;
        public Condition(string name, Func<T, bool> predicate) { Name = name; _predicate = predicate; }
        protected override BtStatus OnTick(T context, float dt) => _predicate(context) ? BtStatus.Success : BtStatus.Failure;
    }

    /// <summary>Leaf wrapping a function. Optional onStart/onReset hooks for stateful leaves.</summary>
    public sealed class Do<T> : BtNode<T>
    {
        private readonly Func<T, float, BtStatus> _tick;
        private readonly Action<T> _onStart;
        private bool _started;

        public Do(string name, Func<T, float, BtStatus> tick, Action<T> onStart = null)
        {
            Name = name; _tick = tick; _onStart = onStart;
        }

        protected override BtStatus OnTick(T context, float dt)
        {
            if (!_started) { _started = true; if (_onStart != null) _onStart(context); }
            var status = _tick(context, dt);
            if (status != BtStatus.Running) _started = false;
            return status;
        }

        public override void Reset() { _started = false; }
    }

    public sealed class Wait<T> : BtNode<T>
    {
        private readonly float _duration;
        private float _elapsed;
        public Wait(float seconds) { Name = "Wait " + seconds.ToString("0.0") + "s"; _duration = seconds; }
        protected override BtStatus OnTick(T context, float dt)
        {
            _elapsed += dt;
            if (_elapsed < _duration) return BtStatus.Running;
            _elapsed = 0f;
            return BtStatus.Success;
        }
        public override void Reset() { _elapsed = 0f; }
    }

    /// <summary>Fails the child if it runs longer than the limit.</summary>
    public sealed class Timeout<T> : BtNode<T>
    {
        private readonly BtNode<T> _child;
        private readonly float _limit;
        private readonly Action<T> _onTimeout;
        private float _elapsed;

        public Timeout(BtNode<T> child, float seconds, Action<T> onTimeout = null)
        {
            _child = child; _limit = seconds; _onTimeout = onTimeout;
            Name = "Timeout(" + seconds.ToString("0") + "s)";
        }

        protected override BtStatus OnTick(T context, float dt)
        {
            _elapsed += dt;
            if (_elapsed > _limit)
            {
                Reset();
                if (_onTimeout != null) _onTimeout(context);
                return BtStatus.Failure;
            }
            var status = _child.Tick(context, dt);
            if (status != BtStatus.Running) _elapsed = 0f;
            return status;
        }

        public override void Reset() { _elapsed = 0f; _child.Reset(); }
        public override string Describe() => _child.Describe();
    }

    /// <summary>Repeats the child until it fails (or forever if it keeps succeeding). Always reports Running unless the child fails.</summary>
    public sealed class RepeatUntilFail<T> : BtNode<T>
    {
        private readonly BtNode<T> _child;
        public RepeatUntilFail(BtNode<T> child) { _child = child; Name = "Repeat"; }
        protected override BtStatus OnTick(T context, float dt)
        {
            var status = _child.Tick(context, dt);
            return status == BtStatus.Failure ? BtStatus.Failure : BtStatus.Running;
        }
        public override void Reset() { _child.Reset(); }
        public override string Describe() => _child.Describe();
    }
}
