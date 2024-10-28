using UnityEngine;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

public class DepthMapProcessorGPU : MonoBehaviour
{
    public string imageFolder = "Assets/Resources/DepthMaps/";
    private const int TEXTURE_SIZE = 128;
    private const int PADDING = 1;
    public Texture3D volumeTexture;
    public List<string[]> frameFiles;
    public int currentFrameIndex = 1;
    public string texturePropertyName = "VolumeTexture";
    public float targetFPS = 60f;

    private VolumeDataset dataset;
    public VolumeRenderedObject volumeRenderer;
    private Texture2D[][] loadedTextures;
    private RenderTexture[] viewRenderTextures;
    private RenderTexture finalRenderTexture;
    private ComputeShader processComputeShader;
    private Material combineMaterial;
    private float nextUpdateTime;

    struct VoxelData
    {
        public int x,
            y,
            z;
        public Color color;
    }

    void Awake()
    {
        LoadFrameFiles();
        LoadAllTextures();
        InitializeRenderTextures();
        CreateInitialVolumeTexture();

        if (volumeRenderer == null)
        {
            Debug.LogError(
                "VolumeRenderedObject is not assigned in the Inspector. Please assign it."
            );
            return;
        }

        dataset = CreateVolumeDataset(
            volumeTexture.width,
            volumeTexture.height,
            volumeTexture.depth
        );
        if (dataset == null)
        {
            Debug.LogError("Failed to create VolumeDataset.");
            return;
        }

        volumeRenderer.dataset = dataset;

        UpdateVolumeTexture();

        nextUpdateTime = Time.time;
    }

    void Update()
    {
        if (Time.time >= nextUpdateTime)
        {
            UpdateVolumeTexture();
            nextUpdateTime = Time.time + (1f / targetFPS);
        }
    }

    private void LoadFrameFiles()
    {
        string[] imageFiles = Directory.GetFiles(imageFolder, "*.png");
        frameFiles = new List<string[]>(imageFiles.Length / 6);

        foreach (string file in imageFiles)
        {
            string fileName = Path.GetFileName(file);
            int frameNumber = int.Parse(fileName.Substring(0, 4));

            while (frameFiles.Count <= frameNumber)
            {
                frameFiles.Add(new string[6]);
            }

            string viewDirection = fileName.Substring(4, 2);
            int viewIndex = GetViewIndex(viewDirection);

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

    private void LoadAllTextures()
    {
        loadedTextures = new Texture2D[frameFiles.Count][];
        for (int frameIndex = 1; frameIndex < frameFiles.Count; frameIndex++)
        {
            loadedTextures[frameIndex] = new Texture2D[6];
            for (int viewIndex = 0; viewIndex < 6; viewIndex++)
            {
                string file = frameFiles[frameIndex][viewIndex];
                if (!string.IsNullOrEmpty(file))
                {
                    byte[] imageData = File.ReadAllBytes(file);
                    loadedTextures[frameIndex][viewIndex] = new Texture2D(
                        TEXTURE_SIZE,
                        TEXTURE_SIZE,
                        TextureFormat.RGBAFloat,
                        false
                    );
                    loadedTextures[frameIndex][viewIndex].LoadImage(imageData);
                }
            }
        }
    }

    private void InitializeRenderTextures()
    {
        viewRenderTextures = new RenderTexture[6];
        for (int i = 0; i < 6; i++)
        {
            viewRenderTextures[i] = new RenderTexture(TEXTURE_SIZE, TEXTURE_SIZE, 0, RenderTextureFormat.ARGBFloat);
        }

        finalRenderTexture = new RenderTexture(TEXTURE_SIZE, TEXTURE_SIZE, 0, RenderTextureFormat.ARGBFloat);

        processComputeShader = Resources.Load<ComputeShader>("ProcessViewsComputeShader");
        combineMaterial = new Material(Shader.Find("Custom/CombineViews"));
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

        volumeData = new NativeArray<Color>(
            TEXTURE_SIZE * TEXTURE_SIZE * TEXTURE_SIZE,
            Allocator.Persistent
        );
        ProcessFrame(1);

        volumeTexture.SetPixelData(volumeData, 0);
        volumeTexture.Apply();
    }

    private VolumeDataset CreateVolumeDataset(int width, int height, int depth)
    {
        VolumeDataset dataset = ScriptableObject.CreateInstance<VolumeDataset>();
        dataset.dimX = width;
        dataset.dimY = height;
        dataset.dimZ = depth;
        dataset.data = new float[width * height * depth * 4]; // 4 times larger to hold RGBA data
        dataset.volumeScale = 1f;
        dataset.scale = Vector3.one;
        dataset.rotation = Quaternion.identity;
        dataset.datasetName = "Generated Dataset";
        return dataset;
    }

    private void UpdateVolumeTexture()
    {
        currentFrameIndex = (currentFrameIndex % (frameFiles.Count - 1)) + 1;

        ClearVolumeTexture();
        ProcessFrame(currentFrameIndex);

        volumeTexture.SetPixelData(volumeData, 0);
        volumeTexture.Apply();

        // Update texture in VolumeRenderedObject
        volumeRenderer.UpdateTexture(volumeTexture);

        // Update dataset data
        UpdateDatasetData();
    }

    private void UpdateDatasetData()
    {
        if (dataset != null)
        {
            for (int i = 0; i < volumeData.Length; i++)
            {
                dataset.data[i] = volumeData[i].r; // Store red channel
                dataset.data[i + volumeData.Length] = volumeData[i].g; // Store green channel
                dataset.data[i + volumeData.Length * 2] = volumeData[i].b; // Store blue channel
                dataset.data[i + volumeData.Length * 3] = volumeData[i].a; // Store alpha channel
            }
            dataset.dataTexture = null; // Force recreation of the texture
        }
    }

    private void ClearVolumeTexture()
    {
        for (int i = 0; i < volumeData.Length; i++)
        {
            volumeData[i] = Color.clear;
        }
    }

    private void ProcessFrame(int frameIndex)
    {
        if (frameFiles == null || frameFiles.Count == 0)
        {
            Debug.LogWarning("No frame files loaded. Cannot process frame.");
            return;
        }

        NativeList<VoxelData> voxelDataList = new NativeList<VoxelData>(Allocator.TempJob);

        for (int viewIndex = 0; viewIndex < 6; viewIndex++)
        {
            if (
                frameIndex >= 1
                && frameIndex <= frameFiles.Count
                && viewIndex < frameFiles[frameIndex].Length
            )
            {
                Texture2D imageTexture = loadedTextures[frameIndex][viewIndex];
                if (imageTexture != null)
                {
                    switch (viewIndex)
                    {
                        case 0:
                            ProcessNXView(imageTexture, voxelDataList);
                            break;
                        case 1:
                            ProcessNZView(imageTexture, voxelDataList);
                            break;
                        case 2:
                            ProcessNYView(imageTexture, voxelDataList);
                            break;
                        case 3:
                            ProcessPXView(imageTexture, voxelDataList);
                            break;
                        case 4:
                            ProcessPZView(imageTexture, voxelDataList);
                            break;
                        case 5:
                            ProcessPYView(imageTexture, voxelDataList);
                            break;
                    }
                }
                else
                {
                    Debug.LogWarning($"Missing texture for frame {frameIndex}, view {viewIndex}");
                }
            }
            else
            {
                Debug.LogWarning(
                    $"Invalid frame or view index: frame {frameIndex}, view {viewIndex}"
                );
            }
        }

        new CombineVoxelsJob
        {
            volumeData = volumeData,
            voxelDataList = voxelDataList,
            textureSize = TEXTURE_SIZE
        }
            .Schedule()
            .Complete();

        voxelDataList.Dispose();
    }

    void ProcessNXView(Texture2D image, NativeList<VoxelData> voxelDataList)
    {
        for (int y = PADDING; y < TEXTURE_SIZE - PADDING; y++)
        {
            for (int z = PADDING; z < TEXTURE_SIZE - PADDING; z++)
            {
                Color pixelColor = image.GetPixel(z, y);
                float depth = 1f - pixelColor.a;
                int x = Mathf.RoundToInt((depth * (TEXTURE_SIZE - 1)));
                voxelDataList.Add(
                    new VoxelData
                    {
                        x = TEXTURE_SIZE - 1 - x,
                        y = y,
                        z = z,
                        color = new Color(pixelColor.r, pixelColor.g, pixelColor.b, depth)
                    }
                );
            }
        }
    }

    void ProcessNYView(Texture2D image, NativeList<VoxelData> voxelDataList)
    {
        for (int x = PADDING; x < TEXTURE_SIZE - PADDING; x++)
        {
            for (int z = PADDING; z < TEXTURE_SIZE - PADDING; z++)
            {
                Color pixelColor = image.GetPixel(z, x);
                float depth = 1f - pixelColor.a;
                int y = Mathf.RoundToInt((depth * (TEXTURE_SIZE - 1)));
                voxelDataList.Add(
                    new VoxelData
                    {
                        x = x,
                        y = TEXTURE_SIZE - 1 - y,
                        z = -z,
                        color = new Color(pixelColor.r, pixelColor.g, pixelColor.b, depth)
                    }
                );
            }
        }
    }

    void ProcessNZView(Texture2D image, NativeList<VoxelData> voxelDataList)
    {
        for (int x = PADDING; x < TEXTURE_SIZE - PADDING; x++)
        {
            for (int y = PADDING; y < TEXTURE_SIZE - PADDING; y++)
            {
                Color pixelColor = image.GetPixel(x, y);
                float depth = 1f - pixelColor.a;
                int z = Mathf.RoundToInt((depth * (TEXTURE_SIZE - 1)));
                int safeZ = Mathf.Clamp(z, 0, TEXTURE_SIZE - 1);
                voxelDataList.Add(
                    new VoxelData
                    {
                        x = x,
                        y = y,
                        z = safeZ,
                        color = new Color(pixelColor.r, pixelColor.g, pixelColor.b, depth)
                    }
                );
            }
        }
    }

    void ProcessPXView(Texture2D image, NativeList<VoxelData> voxelDataList)
    {
        for (int y = PADDING; y < TEXTURE_SIZE - PADDING; y++)
        {
            for (int z = PADDING; z < TEXTURE_SIZE - PADDING; z++)
            {
                Color pixelColor = image.GetPixel(TEXTURE_SIZE - 1 - z, TEXTURE_SIZE - 1 - y);
                float depth = 1f - pixelColor.a;
                int x = Mathf.RoundToInt((depth * (TEXTURE_SIZE - 1)));
                voxelDataList.Add(
                    new VoxelData
                    {
                        x = x,
                        y = TEXTURE_SIZE - 1 - y,
                        z = z,
                        color = new Color(pixelColor.r, pixelColor.g, pixelColor.b, depth)
                    }
                );
            }
        }
    }

    void ProcessPYView(Texture2D image, NativeList<VoxelData> voxelDataList)
    {
        for (int x = PADDING; x < TEXTURE_SIZE - PADDING; x++)
        {
            for (int z = PADDING; z < TEXTURE_SIZE - PADDING; z++)
            {
                Color pixelColor = image.GetPixel(TEXTURE_SIZE - 1 - z, TEXTURE_SIZE - 1 - x);
                float depth = 1f - pixelColor.a;
                int y = Mathf.RoundToInt((depth * (TEXTURE_SIZE - 1)));
                voxelDataList.Add(
                    new VoxelData
                    {
                        x = -z,
                        y = y,
                        z = x,
                        color = new Color(pixelColor.r, pixelColor.g, pixelColor.b, depth)
                    }
                );
            }
        }
    }

    void ProcessPZView(Texture2D image, NativeList<VoxelData> voxelDataList)
    {
        for (int x = PADDING; x < TEXTURE_SIZE - PADDING; x++)
        {
            for (int y = PADDING; y < TEXTURE_SIZE - PADDING; y++)
            {
                Color pixelColor = image.GetPixel(TEXTURE_SIZE - 1 - x, TEXTURE_SIZE - 1 - y);
                float depth = 1f - pixelColor.a;
                int z = Mathf.RoundToInt((depth * (TEXTURE_SIZE - 1)));
                voxelDataList.Add(
                    new VoxelData
                    {
                        x = x,
                        y = TEXTURE_SIZE - 1 - y,
                        z = TEXTURE_SIZE - 1 - z,
                        color = new Color(pixelColor.r, pixelColor.g, pixelColor.b, depth)
                    }
                );
            }
        }
    }

    [BurstCompile]
    struct CombineVoxelsJob : IJob
    {
        public NativeArray<Color> volumeData;
        public NativeList<VoxelData> voxelDataList;
        public int textureSize;

        public void Execute()
        {
            for (int i = 0; i < voxelDataList.Length; i++)
            {
                var voxel = voxelDataList[i];
                if (
                    voxel.x >= 0
                    && voxel.x < textureSize
                    && voxel.y >= 0
                    && voxel.y < textureSize
                    && voxel.z >= 0
                    && voxel.z < textureSize
                )
                {
                    int index =
                        voxel.x + voxel.y * textureSize + voxel.z * textureSize * textureSize;
                    Color existingColor = volumeData[index];

                    if (existingColor.a == 0f)
                    {
                        volumeData[index] = voxel.color;
                    }
                    else
                    {
                        float totalAlpha = existingColor.a + voxel.color.a;
                        Color combinedColor = new Color(
                            (existingColor.r * existingColor.a + voxel.color.r * voxel.color.a)
                                / totalAlpha,
                            (existingColor.g * existingColor.a + voxel.color.g * voxel.color.a)
                                / totalAlpha,
                            (existingColor.b * existingColor.a + voxel.color.b * voxel.color.a)
                                / totalAlpha,
                            totalAlpha
                        );

                        volumeData[index] = combinedColor;
                    }
                }
            }
        }
    }

    void OnDisable()
    {
        if (volumeTexture != null)
        {
            Destroy(volumeTexture);
        }
        if (volumeData.IsCreated)
        {
            volumeData.Dispose();
        }
    }
}