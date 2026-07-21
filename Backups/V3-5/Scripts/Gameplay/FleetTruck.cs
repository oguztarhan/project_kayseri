using Game.Core;
using Game.Data;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// A single pooled truck owned by a <see cref="TruckFleet"/>. The fleet injects source/dest inventories,
    /// speed, capacity, path and a lane offset (for parallel roads). Pulls a load at the source, drives its
    /// lane to the dest, deposits, returns — independent of the other trucks, so adding trucks clears jams
    /// (GDD §3 fleet size). No allocations in Update; movement mirrors <see cref="Hauler"/>.
    /// </summary>
    public sealed class FleetTruck : MonoBehaviour
    {
        private TruckFleet _fleet;
        private GameObject _load;
        private Vector3 _lane;
        private readonly Inventory<ResourceDef> _cargo = new Inventory<ResourceDef>(BigDouble.Zero);

        private enum St { AtSource, ToDest, AtDest, ToSource }
        private St _state = St.AtSource;
        private int _wp;
        private float _cur;

        public void Init(TruckFleet fleet, GameObject load) { _fleet = fleet; _load = load; }
        public void SetLane(Vector3 offset) { _lane = offset; }

        private void Update()
        {
            if (_fleet == null) return;
            Inventory<ResourceDef> src = _fleet.Source, dst = _fleet.Dest;
            if (src == null || dst == null) return;
            if (_load != null) _load.SetActive(_cargo.Total.Mantissa > 0d);

            switch (_state)
            {
                case St.AtSource:
                    if (src.Total.Mantissa <= 0d) { Brake(); return; }
                    double cap = _fleet.Capacity;
                    _cargo.Capacity = new BigDouble(cap);
                    if (Inventories.Transfer(src, _cargo, new BigDouble(cap)).Mantissa <= 0d) { Brake(); return; }
                    _wp = 0; _state = St.ToDest;
                    break;
                case St.ToDest:
                    if (Follow(true)) _state = St.AtDest;
                    break;
                case St.AtDest:
                    Inventories.Transfer(_cargo, dst, _cargo.Total);
                    if (_cargo.Total.Mantissa > 0d) { Brake(); return; }
                    _wp = 0; _state = St.ToSource;
                    break;
                case St.ToSource:
                    if (Follow(false)) _state = St.AtSource;
                    break;
            }
        }

        private bool Follow(bool forward)
        {
            Vector3 target = Waypoint(forward, _wp, out bool last) + _lane;
            if (MoveTo(target, last))
            {
                if (last) return true;
                _wp++;
            }
            return false;
        }

        private Vector3 Waypoint(bool forward, int wp, out bool last)
        {
            Transform[] path = _fleet.Path;
            int n = path != null ? path.Length : 0;
            if (forward)
            {
                if (wp < n && path[wp] != null) { last = false; return path[wp].position; }
                last = true; return _fleet.DestPoint != null ? _fleet.DestPoint.position : transform.position;
            }
            if (wp < n && path[n - 1 - wp] != null) { last = false; return path[n - 1 - wp].position; }
            last = true; return _fleet.SourcePoint != null ? _fleet.SourcePoint.position : transform.position;
        }

        private bool MoveTo(Vector3 target, bool isFinal)
        {
            Vector3 to = target - transform.position; to.y = 0f;
            float dist = to.magnitude;
            if (dist > 0.05f)
            {
                Quaternion look = Quaternion.LookRotation(to.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look, _fleet.TurnSpeed * Time.deltaTime);
            }
            float sp = _fleet.Speed;
            float targetSpeed = (isFinal && dist < 3f) ? Mathf.Lerp(1.5f, sp, dist / 3f) : sp;
            _cur = Mathf.MoveTowards(_cur, targetSpeed, _fleet.Accel * Time.deltaTime);
            transform.position = Vector3.MoveTowards(transform.position, target, _cur * Time.deltaTime);
            return (transform.position - target).sqrMagnitude < 0.06f;
        }

        private void Brake() => _cur = Mathf.MoveTowards(_cur, 0f, _fleet.Accel * 2f * Time.deltaTime);
    }
}
