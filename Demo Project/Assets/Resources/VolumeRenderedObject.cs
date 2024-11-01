using UnityEngine;

public class VolumeRenderedObject : MonoBehaviour
{
    [SerializeField, HideInInspector]
    public VolumeDataset dataset;

    public MeshRenderer meshRenderer;

    [SerializeField, HideInInspector]
    public GameObject volumeContainerObject;

    [SerializeField, HideInInspector]
    private bool rayTerminationEnabled = true;

    [SerializeField, HideInInspector]
    private bool cubicInterpolationEnabled = false;

    void Awake()
    {
        if (meshRenderer == null)
        {
            Debug.LogWarning("MeshRenderer is not assigned in Awake. Will try to assign it later.");
            // Optionally, you could try to find the MeshRenderer here:
            // meshRenderer = GetComponentInChildren<MeshRenderer>();
        }

        if (dataset == null)
        {
            Debug.LogWarning(
                "VolumeDataset is not assigned in Awake. Will try to assign it later."
            );
        }
        else
        {
            UpdateMaterialProperties();
        }
    }

    void OnValidate()
    {
        EnsureVolumeContainerRef();
    }

    private void EnsureVolumeContainerRef()
    {
        if (volumeContainerObject == null)
        {
            Debug.LogWarning(
                "VolumeContainer missing. This is expected if the object was saved with an old version of the plugin. Please re-save it."
            );
            Transform trans = transform.Find("VolumeContainer");
            if (trans == null)
                trans = GetComponentInChildren<MeshRenderer>(true)?.transform;
            if (trans != null)
                volumeContainerObject = trans.gameObject;
        }
    }

    public void UpdateMaterialProperties()
    {
        if (meshRenderer == null)
        {
            Debug.LogError("MeshRenderer is not assigned.");
            return;
        }

        if (dataset == null)
        {
            Debug.LogError("VolumeDataset is not assigned.");
            return;
        }

        if (meshRenderer.sharedMaterial == null)
        {
            meshRenderer.sharedMaterial = new Material(Shader.Find("Custom/SimplifiedMIPShader"));
            meshRenderer.sharedMaterial.SetTexture("_MainTex", dataset.GetDataTexture());
        }

        meshRenderer.sharedMaterial.SetFloat("_MinVal", 0f);
        meshRenderer.sharedMaterial.SetFloat("_MaxVal", 1f);
        meshRenderer.sharedMaterial.SetVector(
            "_TextureSize",
            new Vector3(dataset.dimX, dataset.dimY, dataset.dimZ)
        );

        if (rayTerminationEnabled)
            meshRenderer.sharedMaterial.EnableKeyword("RAY_TERMINATE_ON");
        else
            meshRenderer.sharedMaterial.DisableKeyword("RAY_TERMINATE_ON");

        if (cubicInterpolationEnabled)
            meshRenderer.sharedMaterial.EnableKeyword("CUBIC_INTERPOLATION_ON");
        else
            meshRenderer.sharedMaterial.DisableKeyword("CUBIC_INTERPOLATION_ON");
    }

    public void UpdateTexture(Texture newTexture)
    {
        if (meshRenderer != null)
        {
            meshRenderer.sharedMaterial.SetTexture("_MainTex", newTexture);
        }
    }

    public void SetDataset(VolumeDataset newDataset)
    {
        dataset = newDataset;
        if (dataset != null && meshRenderer != null)
        {
            UpdateMaterialProperties();
        }
    }
}
