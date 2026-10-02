namespace Cascade.AI.Utility
{
    public delegate float InputFn<TContext>(TContext context);

    /// <summary>One scoring factor of a utility option. Must return a score in [0, 1].</summary>
    public interface IConsideration<TContext>
    {
        string Name { get; }
        /// <summary>Returns the score and exposes the raw input for decision traces.</summary>
        float Evaluate(TContext context, out float rawInput);
    }

    /// <summary>Reads a normalized input from the context and passes it through a response curve.</summary>
    public sealed class Consideration<TContext> : IConsideration<TContext>
    {
        public string Name { get; }
        private readonly InputFn<TContext> _input;
        private readonly ResponseCurve _curve;

        public Consideration(string name, InputFn<TContext> input, ResponseCurve curve)
        {
            Name = name;
            _input = input;
            _curve = curve;
        }

        public float Evaluate(TContext context, out float rawInput)
        {
            rawInput = _input(context);
            return _curve.Evaluate(rawInput);
        }
    }
}
