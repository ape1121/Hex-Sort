using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ape.Sounds
{
    [MovedFrom(false, sourceNamespace: "")]
    [CreateAssetMenu(fileName = "AllSounds", menuName = "HexSort/Sounds/AllSounds", order = 1)]
    public class AllSounds : ScriptableObject
    {
        public Sound[] sounds;
    }
}
