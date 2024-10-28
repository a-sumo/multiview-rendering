using UnityEngine;
using UnityEngine.VFX;
using System.Collections;

public class VolumeTextureGenerator : MonoBehaviour
{
    public int size = 128;
    public Texture3D volumeTexture;
    private ComputeShader computeShader;
    private int kernelHandle;
    private ComputeBuffer buffer;
    public VisualEffect visualEffect; // Add this line
    public string texturePropertyName = "VolumeTexture"; // Add this line

    void Awake()
    {
        // Initialize the Compute Shader
        computeShader = Resources.Load<ComputeShader>("VolumeTextureShader");
        kernelHandle = computeShader.FindKernel("CSMain");

        // Create the Texture3D
        volumeTexture = new Texture3D(size, size, size, TextureFormat.RGBAFloat, false);
        volumeTexture.wrapMode = TextureWrapMode.Clamp;
        volumeTexture.filterMode = FilterMode.Point;

        // Create a ComputeBuffer to hold the texture data
        buffer = new ComputeBuffer(size * size * size, sizeof(float) * 4);

        // Set up the Compute Shader
        computeShader.SetInt("_Size", size);
        computeShader.SetBuffer(kernelHandle, "ResultBuffer", buffer);

        // Dispatch the Compute Shader to initialize the texture
        computeShader.Dispatch(kernelHandle, size / 8, size / 8, size / 8);

        // Copy data from ComputeBuffer to Texture3D
        float[] data = new float[size * size * size * 4];
        buffer.GetData(data);
        volumeTexture.SetPixelData(data, 0);
        volumeTexture.Apply();

        // Assign the texture to the VFX Graph
        AssignTextureToVFXGraph();
    }

    void AssignTextureToVFXGraph()
    {
        if (visualEffect != null)
        {
            int texturePropertyID = Shader.PropertyToID(texturePropertyName);
            visualEffect.SetTexture(texturePropertyID, volumeTexture);
        }
        else
        {
            Debug.LogWarning("No VisualEffect component assigned in the inspector.");
        }
    }

    void Update()
    {
        // Update the texture every frame
        float time = Time.time;
        computeShader.SetFloat("_Time", time);
        computeShader.Dispatch(kernelHandle, size / 8, size / 8, size / 8);

        // Copy updated data from ComputeBuffer to Texture3D
        float[] data = new float[size * size * size * 4];
        buffer.GetData(data);
        volumeTexture.SetPixelData(data, 0);
        volumeTexture.Apply();

        // Reassign the texture to the VFX Graph (optional, only if needed)
        AssignTextureToVFXGraph();
    }

    void OnDestroy()
    {
        // Clean up resources
        if (volumeTexture != null)
        {
            Destroy(volumeTexture);
        }
        if (buffer != null)
        {
            buffer.Release();
        }
    }
}
