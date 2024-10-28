using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(VolumeRenderedObject))]
public class VolumeRenderedObjectEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        VolumeRenderedObject volumeRenderedObject = (VolumeRenderedObject)target;

        if (volumeRenderedObject.meshRenderer == null)
        {
            if (GUILayout.Button("Assign MeshRenderer"))
            {
                AssignMeshRenderer(volumeRenderedObject);
            }
        }
    }

    private void AssignMeshRenderer(VolumeRenderedObject volumeRenderedObject)
    {
        MeshRenderer meshRenderer = volumeRenderedObject.GetComponentInChildren<MeshRenderer>();
        if (meshRenderer != null)
        {
            volumeRenderedObject.meshRenderer = meshRenderer;
            EditorUtility.SetDirty(volumeRenderedObject);
            Debug.Log("MeshRenderer assigned successfully.");
        }
        else
        {
            Debug.LogError("No MeshRenderer found in children.");
        }
    }
}
