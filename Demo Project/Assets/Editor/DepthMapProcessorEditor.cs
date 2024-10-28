// using UnityEngine;
// using UnityEditor;

// [CustomEditor(typeof(DepthMapProcessor))]
// public class DepthMapProcessorEditor : Editor
// {
//     public override void OnInspectorGUI()
//     {
//         base.OnInspectorGUI();

//         DepthMapProcessor processor = (DepthMapProcessor)target;

//         if (GUILayout.Button("Process Current Frame and Create Initial Volume Texture"))
//         {
//             processor.ProcessDepthMapsAndCreateInitialVolumeTexture();

//             if (processor.frameFiles == null || processor.frameFiles.Count == 0)
//             {
//                 EditorGUILayout.HelpBox(
//                     "No depth map files found. Please check the image folder path.",
//                     MessageType.Warning
//                 );
//             }
//             else
//             {
//                 EditorUtility.SetDirty(processor);
//                 AssetDatabase.SaveAssets();
//                 Debug.Log(
//                     $"Initial volume texture created and processed for frame {processor.currentFrameIndex}."
//                 );
//             }
//         }
//     }
// }
