using UnityEditor;

namespace Kanameliser.EditorPlus
{
  internal class ToggleObjectsActive
  {
    [MenuItem("Tools/Kanameliser Editor Plus/Toggle objects active %g")]
    private static void ToggleObjectsBetweenActiveAndEditorOnly()
    {

      foreach (var item in Selection.transforms)
      {
        Undo.RecordObject (item.gameObject, "Toggle objects active");

        item.gameObject.tag = item.gameObject.activeSelf ? "EditorOnly" : "Untagged";
        item.gameObject.SetActive(!item.gameObject.activeSelf);
      }
    }
  }
}