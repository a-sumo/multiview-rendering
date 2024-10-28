using UnityEngine;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine.VFX;

public class DepthMapProcessor : MonoBehaviour
{
    public string imageFolder = "Assets/Resources/DepthMaps/";
    private const int TEXTURE_SIZE = 128;
    private const int PADDING = 1;
    public Texture3D volumeTexture;
    public List<string[]> frameFiles;
    public int currentFrameIndex = 1;
    public VisualEffect visualEffect;
    public string texturePropertyName = "VolumeTexture";

    void Awake()
    {
        // Load frame files
        LoadFrameFiles();

        // Create initial volume texture
        CreateInitialVolumeTexture();

        // Assign the texture to the VFX Graph
        AssignTextureToVFXGraph();
    }

    void Update()
    {
        // Update the volume texture
        UpdateVolumeTexture();

        // Reassign the texture to the VFX Graph
        AssignTextureToVFXGraph();
    }

    private void LoadFrameFiles()
    {
        string[] imageFiles = Directory.GetFiles(imageFolder, "*.png");
        frameFiles = new List<string[]>();

        foreach (string file in imageFiles)
        {
            string fileName = Path.GetFileName(file);
            int frameNumber = int.Parse(fileName.Substring(0, 4));

            // Ensure frameFiles has enough capacity
            while (frameFiles.Count <= frameNumber)
            {
                frameFiles.Add(new string[6]);
            }

            string viewDirection = fileName.Substring(4, 2);
            int viewIndex = GetViewIndex(viewDirection);

            // Ensure the array for this frame has enough capacity
            if (frameFiles[frameNumber].Length <= viewIndex)
            {
                string[] temp = new string[6];
                frameFiles[frameNumber].CopyTo(temp, 0);
                frameFiles[frameNumber] = temp;
            }

            frameFiles[frameNumber][viewIndex] = file;
        }
    }

    private int GetViewIndex(string viewDirection)
    {
        switch (viewDirection)
        {
            case "nx":
                return 0;
            case "ny":
                return 1;
            case "nz":
                return 2;
            case "px":
                return 3;
            case "py":
                return 4;
            case "pz":
                return 5;
            default:
                throw new System.ArgumentOutOfRangeException(
                    nameof(viewDirection),
                    viewDirection,
                    null
                );
        }
    }

    public void CreateInitialVolumeTexture()
    {
        volumeTexture = new Texture3D(
            TEXTURE_SIZE,
            TEXTURE_SIZE,
            TEXTURE_SIZE,
            TextureFormat.RGBAFloat,
            false
        );
        volumeTexture.wrapMode = TextureWrapMode.Repeat;
        volumeTexture.filterMode = FilterMode.Point;

        // Initialize all voxels to black with alpha 0
        for (int x = 0; x < TEXTURE_SIZE; x++)
        {
            for (int y = 0; y < TEXTURE_SIZE; y++)
            {
                for (int z = 0; z < TEXTURE_SIZE; z++)
                {
                    volumeTexture.SetPixel(x, y, z, new Color(0f, 0f, 0f, 0f));
                }
            }
        }

        ProcessFrame(1);

        volumeTexture.Apply();
    }

    private void UpdateVolumeTexture()
    {
        currentFrameIndex++;
        if (currentFrameIndex > frameFiles.Count - 1)
        {
            currentFrameIndex = 1;
        }

        // Clear the volume texture
        ClearVolumeTexture();

        // Process the new frame
        ProcessFrame(currentFrameIndex);

        // Apply the changes
        volumeTexture.Apply();
    }

    private void ClearVolumeTexture()
    {
        for (int x = 0; x < TEXTURE_SIZE; x++)
        {
            for (int y = 0; y < TEXTURE_SIZE; y++)
            {
                for (int z = 0; z < TEXTURE_SIZE; z++)
                {
                    volumeTexture.SetPixel(x, y, z, new Color(0f, 0f, 0f, 0f));
                }
            }
        }
    }

    private void ProcessFrame(int frameIndex)
    {
        if (frameFiles == null || frameFiles.Count == 0)
        {
            Debug.LogWarning("No frame files loaded. Cannot process frame.");
            return;
        }

        for (int viewIndex = 0; viewIndex < 6; viewIndex++)
        {
            if (
                frameIndex >= 1
                && frameIndex <= frameFiles.Count
                && viewIndex < frameFiles[frameIndex].Length
            )
            {
                string file = frameFiles[frameIndex][viewIndex];
                if (!string.IsNullOrEmpty(file))
                {
                    byte[] imageData = File.ReadAllBytes(file);
                    Texture2D imageTexture = new Texture2D(
                        TEXTURE_SIZE,
                        TEXTURE_SIZE,
                        TextureFormat.RGBAFloat,
                        false
                    );
                    imageTexture.LoadImage(imageData);
                    switch (viewIndex)
                    {
                        case 0:
                            ProcessNXView(imageTexture);
                            break;
                        case 1:
                            ProcessNZView(imageTexture);
                            break;
                        case 2:
                            ProcessNYView(imageTexture);
                            break;
                        case 3:
                            ProcessPXView(imageTexture);
                            break;
                        case 4:
                            ProcessPZView(imageTexture);
                            break;
                        case 5:
                            ProcessPYView(imageTexture);
                            break;
                    }
                }
                else
                {
                    Debug.LogWarning($"Missing file for frame {frameIndex}, view {viewIndex}");
                }
            }
            else
            {
                Debug.LogWarning(
                    $"Invalid frame or view index: frame {frameIndex}, view {viewIndex}"
                );
            }
        }
    }

    void ProcessNXView(Texture2D image)
    {
        for (int y = PADDING; y < TEXTURE_SIZE - PADDING; y++)
        {
            for (int z = PADDING; z < TEXTURE_SIZE - PADDING; z++)
            {
                Color pixelColor = image.GetPixel(z, y);
                float depth = 1f - pixelColor.a;
                int x = Mathf.RoundToInt((depth * (TEXTURE_SIZE - 1)));
                CombineVoxel(
                    TEXTURE_SIZE - 1 - x,
                    y,
                    z,
                    new Color(pixelColor.r, pixelColor.g, pixelColor.b, depth)
                );
            }
        }
    }

    void ProcessNYView(Texture2D image)
    {
        for (int x = PADDING; x < TEXTURE_SIZE - PADDING; x++)
        {
            for (int z = PADDING; z < TEXTURE_SIZE - PADDING; z++)
            {
                Color pixelColor = image.GetPixel(z, x);
                float depth = 1f - pixelColor.a;
                int y = Mathf.RoundToInt((depth * (TEXTURE_SIZE - 1)));
                CombineVoxel(
                    x,
                    TEXTURE_SIZE - 1 - y,
                    -z,
                    new Color(pixelColor.r, pixelColor.g, pixelColor.b, depth)
                );
            }
        }
    }

    void ProcessNZView(Texture2D image)
    {
        for (int x = PADDING; x < TEXTURE_SIZE - PADDING; x++)
        {
            for (int y = PADDING; y < TEXTURE_SIZE - PADDING; y++)
            {
                Color pixelColor = image.GetPixel(x, y);
                float depth = 1f - pixelColor.a;
                int z = Mathf.RoundToInt((depth * (TEXTURE_SIZE - 1)));
                CombineVoxel(
                    x,
                    y,
                    -(TEXTURE_SIZE - 1 - z),
                    new Color(pixelColor.r, pixelColor.g, pixelColor.b, depth)
                );
            }
        }
    }

    void ProcessPXView(Texture2D image)
    {
        for (int y = PADDING; y < TEXTURE_SIZE - PADDING; y++)
        {
            for (int z = PADDING; z < TEXTURE_SIZE - PADDING; z++)
            {
                Color pixelColor = image.GetPixel(TEXTURE_SIZE - 1 - z, TEXTURE_SIZE - 1 - y);
                float depth = 1f - pixelColor.a;
                int x = Mathf.RoundToInt((depth * (TEXTURE_SIZE - 1)));
                CombineVoxel(
                    x,
                    TEXTURE_SIZE - 1 - y,
                    z,
                    new Color(pixelColor.r, pixelColor.g, pixelColor.b, depth)
                );
            }
        }
    }

    void ProcessPYView(Texture2D image)
    {
        for (int x = PADDING; x < TEXTURE_SIZE - PADDING; x++)
        {
            for (int z = PADDING; z < TEXTURE_SIZE - PADDING; z++)
            {
                Color pixelColor = image.GetPixel(TEXTURE_SIZE - 1 - z, TEXTURE_SIZE - 1 - x);
                float depth = 1f - pixelColor.a;
                int y = Mathf.RoundToInt((depth * (TEXTURE_SIZE - 1)));
                CombineVoxel(-z, y, x, new Color(pixelColor.r, pixelColor.g, pixelColor.b, depth));
            }
        }
    }

    void ProcessPZView(Texture2D image)
    {
        for (int x = 1; x < TEXTURE_SIZE; x++)
        {
            for (int y = 1; y < TEXTURE_SIZE - 1; y++)
            {
                Color pixelColor = image.GetPixel(TEXTURE_SIZE - 1 - x, TEXTURE_SIZE - 1 - y);
                float depth = 1f - pixelColor.a;
                int z = Mathf.RoundToInt((depth * (TEXTURE_SIZE - 1)));
                CombineVoxel(
                    x,
                    TEXTURE_SIZE - 1 - y,
                    TEXTURE_SIZE - 1 - z,
                    new Color(pixelColor.r, pixelColor.g, pixelColor.b, depth)
                );
            }
        }
    }

    void CombineVoxel(int x, int y, int z, Color newColor)
    {
        Color existingColor = volumeTexture.GetPixel(x, y, z);

        if (existingColor.a == 0f)
        {
            volumeTexture.SetPixel(x, y, z, newColor);
        }
        else
        {
            float totalAlpha = existingColor.a + newColor.a;
            Color combinedColor = new Color(
                (existingColor.r * existingColor.a + newColor.r * newColor.a) / totalAlpha,
                (existingColor.g * existingColor.a + newColor.g * newColor.a) / totalAlpha,
                (existingColor.b * existingColor.a + newColor.b * newColor.a) / totalAlpha,
                totalAlpha
            );

            volumeTexture.SetPixel(x, y, z, combinedColor);
        }
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

    void OnDisable()
    {
        if (volumeTexture != null)
        {
            Destroy(volumeTexture);
        }
    }
}
