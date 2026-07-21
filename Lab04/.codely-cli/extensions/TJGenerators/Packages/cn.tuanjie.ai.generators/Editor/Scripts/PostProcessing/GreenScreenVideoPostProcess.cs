#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using TJGenerators.Utils;

namespace TJGenerators.Pipeline
{
    /// <summary>
    /// 绿幕视频后处理：创建 ChromaKey 材质，配合 VideoPlayer + RenderTexture 实时抠像。
    /// 不抽帧，不转码——保持原视频不变，播放时通过 shader 实时绿幕抠除。
    /// </summary>
    public static class GreenScreenVideoPostProcess
    {
        private const string ChromaKeyShaderName = "TJGenerators/ChromaKey";

        /// <summary>
        /// 为绿幕视频创建 ChromaKey 材质。
        /// 使用方式：VideoPlayer.renderMode = APIOnly/RenderTexture → 材质赋给 Quad → shader 实时抠除绿色背景。
        /// </summary>
        public static PostProcessResult ProcessVideo(string videoAssetPath, string outputFolder)
        {
            var result = new PostProcessResult();

            if (string.IsNullOrEmpty(videoAssetPath))
            {
                result.Error = "Video asset path is empty";
                return result;
            }

            string videoAbsPath = PathUtils.ToAbsoluteAssetPath(videoAssetPath);
            if (!File.Exists(videoAbsPath))
            {
                result.Error = $"Video file not found: {videoAbsPath}";
                return result;
            }

            PathUtils.EnsureAssetFolder(outputFolder);

            // 查找 ChromaKey shader
            var shader = Shader.Find(ChromaKeyShaderName);
            if (shader == null)
            {
                // Fallback: 通过 AssetDatabase 查找
                string[] guids = AssetDatabase.FindAssets("ChromaKey t:Shader");
                if (guids.Length > 0)
                {
                    string shaderPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                    shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
                }
            }

            if (shader == null)
            {
                result.Error = $"ChromaKey shader '{ChromaKeyShaderName}' not found";
                return result;
            }

            // 创建材质
            string effectName = Path.GetFileNameWithoutExtension(videoAssetPath);
            string materialPath = $"{outputFolder}/{effectName}_ChromaKey.mat";
            materialPath = AssetDatabase.GenerateUniqueAssetPath(materialPath);

            var mat = new Material(shader);
            mat.name = effectName + "_ChromaKey";

            // 默认参数（与 BuildGreenScreenCutoutTexture 算法的默认值对齐）
            mat.SetFloat("_ChromaTolerance", 0.16f);
            mat.SetFloat("_ChromaFeather", 0.04f);
            mat.SetFloat("_SpillRemoval", 0.7f);

            AssetDatabase.CreateAsset(mat, materialPath);
            AssetDatabase.SaveAssets();

            TJLog.Log($"[GreenScreenPostProcess] ChromaKey material created: {materialPath}");

            result.Success = true;
            result.MaterialPath = materialPath;
            result.VideoPath = videoAssetPath;
            result.ShaderName = ChromaKeyShaderName;

            return result;
        }

        public struct PostProcessResult
        {
            public bool Success;
            public string Error;
            public string VideoPath;
            public string MaterialPath;
            public string ShaderName;
        }
    }
}
#endif
