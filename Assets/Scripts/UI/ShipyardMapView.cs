using Game.Core;
using Game.Systems;
using UnityEngine;

namespace Game.UI
{
    public sealed class ShipyardMapView : MonoBehaviour
    {
        public Transform mapRoot;
        public Transform anchorRoot;
        public TextAsset manifestAsset;
        public GameObject[] buildings;
        public GameObject[] lockedPads;
        public PortraitShipyardCamera portraitCamera;
        [SerializeField] private bool usePlayerSave;
        public ShipyardMapManifest Manifest { get; private set; }
        public ShipyardProgression Progress { get; private set; }

        private void Awake()
        {
            var bootstrapSave = ServiceLocator.Get<SaveData>();
            var save = usePlayerSave || ShipyardFeatureSwitch.IsEnabled(bootstrapSave)
                ? bootstrapSave
                : null;
            // Opening the art preview without Bootstrap must not create or overwrite a player save.
            if (save != null) Progress = save.shipyard ?? (save.shipyard = new ShipyardProgression());
            else Progress = new ShipyardProgression();
            Progress.Normalize();
            Apply(Progress);
        }

        public void Apply(ShipyardProgression progress)
        {
            Progress = progress;
            Manifest = JsonUtility.FromJson<ShipyardMapManifest>(manifestAsset.text);
            for (int i = 0; i < Manifest.zones.Length; i++)
            {
                bool built = !Manifest.zones[i].needsArt && progress.IsBuilt(Manifest.zones[i].id);
                if (buildings[i] != null) buildings[i].SetActive(built);
                if (lockedPads[i] != null) lockedPads[i].SetActive(!built);
            }
        }

        public Transform FindAnchor(string id) => anchorRoot != null ? anchorRoot.Find(id) : null;

        public void FocusCannon()
        {
            var anchor = FindAnchor("Station_Cannon_Work");
            if (anchor != null) portraitCamera.Focus(anchor.position);
        }

        public void FocusNext()
        {
            var id = Progress != null ? Progress.NextMachine : "Station_Hull";
            FocusMachine(id);
        }

        public void FocusMachine(string machineId)
        {
            if (string.IsNullOrEmpty(machineId)) return;
            var anchor = FindAnchor(machineId + "_Work");
            if (anchor != null) portraitCamera.Focus(anchor.position);
        }
    }
}
