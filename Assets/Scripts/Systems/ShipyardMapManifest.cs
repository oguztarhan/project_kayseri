using System;
using UnityEngine;

namespace Game.Systems
{
    /// <summary>Array-shaped copy of Claude's measured manifest for Unity JsonUtility.</summary>
    [Serializable]
    public sealed class ShipyardMapManifest
    {
        public Anchor[] anchors;
        public Route[] routes;
        public Zone[] zones;
        public Vector3 boundsMin, boundsMax;
        [Serializable] public sealed class Anchor { public string id; public Vector3 position; }
        [Serializable] public sealed class Route
        {
            public string id, from, to, kind;
            public Vector3[] points;
        }
        [Serializable] public sealed class Zone
        {
            public string id, artGroup;
            public bool needsArt;
            public Vector3 centre;
            public Vector2 size;
        }
    }
}
