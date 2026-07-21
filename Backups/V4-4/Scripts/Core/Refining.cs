namespace Game.Core
{
    /// <summary>
    /// Pure recipe-processing math for the refinery (GDD §4). Converts inputs → output as a continuous
    /// flow limited by available inputs, output space, and a per-step batch cap (the refinery's rate).
    /// Generic over the resource key so it's unit-testable without Unity assets. Handles 1:1 (Coke) and
    /// combine (Steel = Iron + Coal) recipes alike.
    /// </summary>
    public static class Refining
    {
        /// <summary>
        /// Run up to <paramref name="maxBatches"/> of a recipe: consume each input from <paramref name="input"/>
        /// and add the output to <paramref name="output"/>. Returns the number of batches actually produced.
        /// </summary>
        public static BigDouble Process<TKey>(
            Inventory<TKey> input,
            Inventory<TKey> output,
            (TKey key, BigDouble amount)[] inputs,
            TKey outputKey,
            BigDouble outputAmount,
            BigDouble maxBatches) where TKey : class
        {
            if (inputs == null || outputKey == null) return BigDouble.Zero;

            BigDouble batches = maxBatches;

            // Limit by each input's availability.
            for (int i = 0; i < inputs.Length; i++)
            {
                BigDouble need = inputs[i].amount;
                if (need.Mantissa <= 0d) continue;
                BigDouble allowed = input.Get(inputs[i].key) / need;
                if (allowed < batches) batches = allowed;
            }

            // Limit by output space.
            if (outputAmount.Mantissa > 0d)
            {
                BigDouble allowed = output.Space / outputAmount;
                if (allowed < batches) batches = allowed;
            }

            if (batches.Mantissa <= 0d) return BigDouble.Zero;

            for (int i = 0; i < inputs.Length; i++)
                input.Remove(inputs[i].key, inputs[i].amount * batches);

            output.Add(outputKey, outputAmount * batches);
            return batches;
        }
    }
}
