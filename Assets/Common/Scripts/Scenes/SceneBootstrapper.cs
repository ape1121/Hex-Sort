using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ape.Scenes
{
    [MovedFrom(false, sourceNamespace: "")]
    public class SceneBootstrapper : MonoBehaviour
    {
        protected virtual void BootstrapScene()
        {
        }

        private void Start()
        {
            BootstrapScene();
        }
    }
}
