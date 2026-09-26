using System.Collections.Generic;
using UnityEngine;

namespace TheDeep.Data
{
    /// <summary>Something in the world a diver can scan for data (a creature, a rock formation, evidence...).</summary>
    public class ScanTarget : MonoBehaviour
    {
        static readonly Dictionary<int, ScanTarget> all = new();

        [SerializeField, Tooltip("Unique in the scene.")] int id;
        [SerializeField] string title = "UNKNOWN";
        [SerializeField] DataCategory category;
        [SerializeField, Tooltip("Credits when submitted.")] int value = 50;
        [SerializeField] float scanSeconds = 3f;

        public int Id => id;
        public string Title => title;
        public DataCategory Category => category;
        public int Value => value;
        public float ScanSeconds => scanSeconds;
        public string ClassLabel => ClassFor(value);

        public static string ClassFor(int credits) => credits >= 120 ? "CLASS A" : credits >= 70 ? "CLASS B" : "CLASS C";

        public static bool TryGet(int targetId, out ScanTarget target) => all.TryGetValue(targetId, out target);

        void OnEnable() => all[id] = this;

        void OnDisable()
        {
            if (all.TryGetValue(id, out var existing) && existing == this) all.Remove(id);
        }
    }
}
