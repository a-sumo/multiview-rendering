// using UnityEngine;
// using UnityEditor;
// using System.IO;

// [CustomEditor(typeof(DepthMapProcessorJob))]
// public class DepthMapProcessorJobEditor : Editor
// {
//     public override void OnInspectorGUI()
//     {
//         base.OnInspectorGUI();

//         DepthMapProcessorJob processor = (DepthMapProcessorJob)target;

//         GUILayout.Space(20);

//         if (GUILayout.Button("Process and Save All Frames"))
//         {
//             EditorUtility.DisplayProgressBar("Processing Frames", "Starting...", 0f);
//             try
//             {
//                 processor.ProcessAndSaveAllFrames();
//             }
//             finally
//             {
//                 EditorUtility.ClearProgressBar();
//             }
//         }

//         GUILayout.Space(10);

//         if (GUILayout.Button("Load Processed Frames"))
//         {
//             processor.LoadProcessedFrame(processor.currentFrameIndex);
//         }
//     }
// }
