using UnityEditor.Experimental.GraphView;

namespace Lament.SceneGraph
{
    /// <summary>
    /// Concrete GraphView. GraphView is abstract in this editor version, so the window
    /// cannot instantiate it directly; all setup still lives in the window.
    /// </summary>
    public sealed class SceneGraphView : GraphView
    {
    }
}
