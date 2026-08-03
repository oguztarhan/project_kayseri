using System;
using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// The island's authored route network, exported from the Blender generator
    /// (<c>Tools/blender/isomap/14_routes.py</c>) as JSON beside the FBX.
    ///
    /// This is the bridge between the authored map and the gameplay layer: trains
    /// and trucks follow these polylines instead of a runtime-generated layout, so
    /// they stay on the roads and rails that are actually visible. Centrelines are
    /// sampled through the same curve function the mesh builder uses, and are
    /// already in Unity space - no axis conversion here.
    ///
    /// One file per phase; the phase's track and spur set differ.
    /// </summary>
    [Serializable]
    public sealed class IslandRoutes
    {
        [Serializable]
        public sealed class Vec
        {
            public float x;
            public float y;
            public float z;

            public Vector3 ToVector3() => new Vector3(x, y, z);
        }

        [Serializable]
        public sealed class Anchor
        {
            public string name;
            public Vec pos;
        }

        [Serializable]
        public sealed class Path
        {
            public string name;
            public bool closed;
            /// <summary>Carriageway width at this phase. 0 for rail and the ship lane.</summary>
            public float width;
            public Vec[] points;
        }

        public int phase;
        public float roadHeight;
        public float railHeight;
        public float roadWidth;
        public float districtRadius;
        public string[] activeSites;
        public Anchor[] anchors;
        public Path[] paths;

        /// <summary>Parses an exported routes file. Returns null and logs if unusable.</summary>
        public static IslandRoutes Parse(TextAsset json)
        {
            if (json == null)
            {
                Debug.LogWarning("[IslandRoutes] No routes asset assigned.");
                return null;
            }

            IslandRoutes r;
            try
            {
                r = JsonUtility.FromJson<IslandRoutes>(json.text);
            }
            catch (Exception e)
            {
                Debug.LogError("[IslandRoutes] " + json.name + " failed to parse: " + e.Message);
                return null;
            }

            if (r == null || r.paths == null || r.paths.Length == 0)
            {
                Debug.LogError("[IslandRoutes] " + json.name + " has no paths.");
                return null;
            }
            return r;
        }

        /// <summary>World position of a named anchor (mine, depot, refinery, market, port, ...).</summary>
        public bool TryGetAnchor(string anchorName, out Vector3 world)
        {
            if (anchors != null)
            {
                for (int i = 0; i < anchors.Length; i++)
                {
                    if (anchors[i] != null && anchors[i].name == anchorName && anchors[i].pos != null)
                    {
                        world = anchors[i].pos.ToVector3();
                        return true;
                    }
                }
            }
            world = Vector3.zero;
            return false;
        }

        /// <summary>
        /// A named route as world points ("loop", "rail", "railPort", "roadX",
        /// "roadY", "portRoad", "shipLane", "Spur.Mine", ...). Null when the phase
        /// does not have that route - phase 1 has no "railPort", for instance.
        /// </summary>
        public Vector3[] GetPath(string pathName)
        {
            if (paths == null) return null;
            for (int i = 0; i < paths.Length; i++)
            {
                var p = paths[i];
                if (p == null || p.name != pathName || p.points == null) continue;

                var outPts = new Vector3[p.points.Length];
                for (int k = 0; k < outPts.Length; k++) outPts[k] = p.points[k].ToVector3();
                return outPts;
            }
            return null;
        }

        /// <summary>True once the named secondary site has unlocked at this phase.</summary>
        public bool SiteActive(string site)
        {
            if (activeSites == null) return false;
            for (int i = 0; i < activeSites.Length; i++)
                if (activeSites[i] == site) return true;
            return false;
        }
    }
}
