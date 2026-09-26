using UnityEngine;

namespace SIHKH.Core
{
    /// <summary>
    /// Marker: this renderer keeps its material's own colour when the owner tints itself
    /// (player seat colours, enemy tints). Put it on guns, eyes, anything that shouldn't
    /// change with the body.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class KeepMaterialColor : MonoBehaviour
    {
    }
}
