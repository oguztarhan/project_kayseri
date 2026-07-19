using System;
using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// A refinery recipe: consume the listed inputs → produce an output over <see cref="RefineSeconds"/>
    /// (GDD §4). Supports 1:1 (Coke = Coal) and combine recipes (Steel = Iron + Coal). Ingredients use
    /// <see cref="ResourceDef"/> so both ore and intermediate products can be inputs.
    /// </summary>
    [CreateAssetMenu(fileName = "Recipe", menuName = "Ore Empire/Recipe", order = 4)]
    public sealed class Recipe : ScriptableObject
    {
        [Serializable]
        public struct Ingredient
        {
            public ResourceDef resource;
            public double amount;
        }

        [SerializeField] private string displayName = "Recipe";
        [SerializeField] private Ingredient[] inputs;
        [SerializeField] private Product output;
        [SerializeField] private double outputAmount = 1d;
        [SerializeField] private double refineSeconds = 1d;

        public string DisplayName => displayName;
        public Ingredient[] Inputs => inputs;
        public Product Output => output;
        public double OutputAmount => outputAmount;
        public double RefineSeconds => refineSeconds < 0.1d ? 0.1d : refineSeconds;
    }
}
