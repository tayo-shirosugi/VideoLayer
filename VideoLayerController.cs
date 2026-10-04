using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VideoLayer
{
    internal sealed class VideoLayerController : MonoBehaviour
    {
        private const string FrontFileName = "front.mp4";
        private const string BackFileName = "back.mp4";
        private const string CameraPlusMainCameraName = "cameraplus.json";

        private static readonly string[] DebugShaderNames = new string[]
        {
            "VideoLayer/ChromaKey",
            "VideoLayer/GreenKey",
            "VideoLayer/Opaque",
            "VideoLayer/PureAdditive"
        };
        private int _currentShaderIndex = 0;

        private readonly VideoSlot _front = new VideoSlot("Front");
        private readonly VideoSlot _back = new VideoSlot("Back");

        private string _songFolder;
        private SongVideoLayerConfig _songConfig;
        private AudioTimeSyncController _audioTimeSync;
        private Camera _trackedCamera;

        private float _lastSongTime;
        private float _lastSongAdvanceRealtime;
        private float _smoothedSongRate = 1f;
        private bool _haveSongSample;
        private int _lastCameraCount = 0;
        private float _lastCameraSyncRealtime = 0f;
        private int _cameraLayer;
        private readonly Dictionary<Camera, bool> _originalLayerVisibility = new Dictionary<Camera, bool>();
        private static Type _camera2ControllerType;

        private void Awake()
        {
            _cameraLayer = Mathf.Clamp(Plugin.Config.CameraOnlyLayer, 0, 31);
            if (Plugin.Config.DebugLog)
            {
                DumpAllGameShaders();
            }

            SceneManager.activeSceneChanged += OnSceneChanged;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            Camera.onPreCull += OnCameraPreCull;

            var frontShader = ResolveShader(Plugin.Config.FrontBlendMode);
            var backShader = ResolveShader(Plugin.Config.BackBlendMode);

            _front.Initialize(transform, Plugin.Config.CameraOnlyLayer, Plugin.Config.RenderWidth, Plugin.Config.RenderHeight, frontShader, Plugin.Config.FrontRenderQueue);
            _back.Initialize(transform, Plugin.Config.CameraOnlyLayer, Plugin.Config.RenderWidth, Plugin.Config.RenderHeight, backShader, Plugin.Config.BackRenderQueue);
        }

        private void OnDestroy()
        {
            SceneManager.activeSceneChanged -= OnSceneChanged;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            Camera.onPreCull -= OnCameraPreCull;
            StopAndHideAll();
            _front.Dispose();
            _back.Dispose();
        }

        private void OnSceneChanged(Scene from, Scene to)
        {
            if (to.name != "GameCore")
            {
                Plugin.Log.Info($"Active scene changed to '{to.name}'. Stopping video layers.");
                StopAndHideAll();
            }
        }

        private void OnSceneUnloaded(Scene scene)
        {
            if (scene.name == "GameCore")
            {
                Plugin.Log.Info("GameCore scene unloaded. Stopping video layers.");
                StopAndHideAll();
            }
        }

        private void OnCameraPreCull(Camera cam)
        {
            if (Plugin.Config.Enabled && _audioTimeSync != null)
                SyncCameraCullingMask(cam, _trackedCamera);

            if (cam == _trackedCamera && Plugin.Config.Enabled && _audioTimeSync != null)
            {
                UpdateFrontGeometry();
                UpdateBackGeometry();
            }
        }

        internal void PrepareForLevel(BeatmapLevel level)
        {
            StopAndHideAll();
            Plugin.ReloadConfig();
            _cameraLayer = Mathf.Clamp(Plugin.Config.CameraOnlyLayer, 0, 31);
            _front.SetCameraLayer(_cameraLayer);
            _back.SetCameraLayer(_cameraLayer);
            _currentShaderIndex = 0;
            Plugin.Log.Info($"PrepareForLevel called: songName='{level?.songName}', levelID='{level?.levelID}'");
            if (!Plugin.Config.Enabled)
            {
                SetSongFolder(null);
                return;
            }

            var folder = FindCustomSongFolder(level);
            SetSongFolder(folder);
            ApplyConfiguredShaders();
        }

        private void ApplyConfiguredShaders()
        {
            var frontMode = _songConfig?.frontBlendMode ?? _songConfig?.FrontBlendMode ?? Plugin.Config.FrontBlendMode;
            var backMode = _songConfig?.backBlendMode ?? _songConfig?.BackBlendMode ?? Plugin.Config.BackBlendMode;

            var frontShader = ResolveShader(frontMode);
            var backShader = ResolveShader(backMode);

            _front.ApplyShader(frontShader, Plugin.Config.FrontRenderQueue);
            _back.ApplyShader(backShader, Plugin.Config.BackRenderQueue);
            Plugin.Log.Info($"Applied shaders for level: Front='{frontShader?.name}', Back='{backShader?.name}'");

            if (backShader != null)
            {
                var idx = Array.FindIndex(DebugShaderNames, s => string.Equals(s, backShader.name, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                    _currentShaderIndex = idx;
            }
        }

        internal void AttachAudioTimeSync(AudioTimeSyncController audio)
        {
            if (!Plugin.Config.Enabled)
            {
                StopAndHideAll();
                return;
            }

            _audioTimeSync = audio;
            _haveSongSample = false;
            _lastSongTime = 0f;
            _smoothedSongRate = 1f;
            _lastSongAdvanceRealtime = Time.realtimeSinceStartup;

            // Ensure configured shaders are applied at audio start
            ApplyConfiguredShaders();

            TryFindTrackedCamera(force: true);
            _front.SetActive(true);
            _back.SetActive(true);

            Plugin.Log.Info($"AudioTimeSyncController attached (songTime={audio.songTime})");
        }

        internal void DetachAudioTimeSync(AudioTimeSyncController audio)
        {
            if (_audioTimeSync != audio && _audioTimeSync != null)
                return;

            StopAndHideAll();
        }

        internal void StopAndHideAll()
        {
            Plugin.Log.Info("StopAndHideAll: cleanly terminating video layers and hiding quads.");
            _front.StopAndHide(transform);
            _back.StopAndHide(transform);
            RestoreCameraCullingMasks();
            _audioTimeSync = null;
            _haveSongSample = false;
            _trackedCamera = null;
        }

        private void Update()
        {
            if (!Plugin.Config.Enabled || _audioTimeSync == null)
                return;

            var songTime = Mathf.Max(0f, _audioTimeSync.songTime);
            var songLength = _audioTimeSync.songLength;
            var songEndTime = _audioTimeSync.songEndTime;
            var effectiveEnd = songEndTime > 0.01f ? songEndTime : songLength;

            // If the song has completed, cleanly hide the quads before transition to results screen
            if (effectiveEnd > 0.1f && songTime >= effectiveEnd - 0.05f)
            {
                Plugin.Log.Info($"Song reached end (time={songTime:F2}, end={effectiveEnd:F2}). Terminating video layers.");
                StopAndHideAll();
                return;
            }

            _front.CheckPrepared();
            _back.CheckPrepared();

            var now = Time.realtimeSinceStartup;

            bool songAdvancing;
            if (!_haveSongSample)
            {
                _haveSongSample = true;
                _lastSongTime = songTime;
                _lastSongAdvanceRealtime = now;
                songAdvancing = songTime > 0.001f;
            }
            else
            {
                var ds = songTime - _lastSongTime;
                var dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);

                if (ds > 0.0005f)
                {
                    var instantaneousRate = Mathf.Clamp(ds / dt, 0.05f, 4f);
                    _smoothedSongRate = Mathf.Lerp(_smoothedSongRate, instantaneousRate, 0.18f);
                    _lastSongAdvanceRealtime = now;
                }
                else if (ds < -0.02f)
                {
                    // Restart/seek backwards.
                    _lastSongAdvanceRealtime = now;
                    _front.Seek(songTime);
                    _back.Seek(songTime);
                }

                songAdvancing = (now - _lastSongAdvanceRealtime) < Plugin.Config.PauseDetectionSeconds;
                _lastSongTime = songTime;
            }

            if (songTime <= 0.001f)
                songAdvancing = false;

            _front.Sync(songTime, songAdvancing, _smoothedSongRate, Plugin.Config.SyncThresholdSeconds);
            _back.Sync(songTime, songAdvancing, _smoothedSongRate, Plugin.Config.SyncThresholdSeconds);

            HandleDebugKeys();
        }

        private void LateUpdate()
        {
            if (!Plugin.Config.Enabled || _audioTimeSync == null)
                return;

            if (!TryFindTrackedCamera(force: false))
            {
                _front.HideGeometry();
                _back.HideGeometry();
                return;
            }

            // Ensure layer bit is always kept on the active tracked camera (Zero GC alloc)
            var bit = 1 << _cameraLayer;
            if (_trackedCamera != null && (_trackedCamera.cullingMask & bit) == 0)
            {
                _trackedCamera.cullingMask |= bit;
            }

            // Periodically sync all cameras only if camera count changed or interval elapsed (avoid per-frame GC alloc)
            var now = Time.realtimeSinceStartup;
            var currentCameraCount = Camera.allCamerasCount;
            if (currentCameraCount != _lastCameraCount || (now - _lastCameraSyncRealtime) > 2.5f)
            {
                _lastCameraCount = currentCameraCount;
                _lastCameraSyncRealtime = now;
                SyncCameraCullingMasks(Camera.allCameras, _trackedCamera);
            }

            UpdateFrontGeometry();
            UpdateBackGeometry();
        }

        private void UpdateFrontGeometry()
        {
            if (!_front.Ready || _front.Quad == null || _trackedCamera == null)
                return;

            var depth = Mathf.Max(_trackedCamera.nearClipPlane + 0.02f, GetCurrentFrontDistance());
            depth = Mathf.Min(depth, _trackedCamera.farClipPlane - 0.05f);
            _front.UpdateGeometry(_trackedCamera, depth, Plugin.Config.Overscan);
        }

        private void UpdateBackGeometry()
        {
            if (!_back.Ready || _back.Quad == null || _trackedCamera == null)
                return;

            var anchorCamera = Camera.main;
            float avatarDepth;

            if (anchorCamera != null)
            {
                var toAvatar = anchorCamera.transform.position - _trackedCamera.transform.position;
                avatarDepth = Vector3.Dot(toAvatar, _trackedCamera.transform.forward);

                if (avatarDepth < 0.25f)
                    avatarDepth = Vector3.Distance(anchorCamera.transform.position, _trackedCamera.transform.position);
            }
            else
            {
                avatarDepth = 2.0f;
            }

            var depth = Mathf.Max(_trackedCamera.nearClipPlane + 0.25f,
                avatarDepth + Mathf.Max(0.05f, GetCurrentBackOffset()));
            depth = Mathf.Min(depth, _trackedCamera.farClipPlane - 0.05f);

            _back.UpdateGeometry(_trackedCamera, depth, Plugin.Config.Overscan);
        }

        private float GetCurrentBackOffset()
        {
            if (_songConfig != null)
            {
                if (!float.IsNaN(_songConfig.backOffsetMeters) && _songConfig.backOffsetMeters > 0f)
                    return _songConfig.backOffsetMeters;
                if (!float.IsNaN(_songConfig.BackOffsetMeters) && _songConfig.BackOffsetMeters > 0f)
                    return _songConfig.BackOffsetMeters;
            }
            return Plugin.Config.BackOffsetMeters;
        }

        private float GetCurrentFrontDistance()
        {
            if (_songConfig != null)
            {
                if (!float.IsNaN(_songConfig.frontDistanceMeters) && _songConfig.frontDistanceMeters > 0f)
                    return _songConfig.frontDistanceMeters;
                if (!float.IsNaN(_songConfig.FrontDistanceMeters) && _songConfig.FrontDistanceMeters > 0f)
                    return _songConfig.FrontDistanceMeters;
            }
            return Plugin.Config.FrontDistanceMeters;
        }

        private static bool IsCameraPlusCamera(Camera cam)
        {
            if (cam == null) return false;
            if (string.Equals(cam.name, CameraPlusMainCameraName, StringComparison.OrdinalIgnoreCase)) return true;
            return !string.IsNullOrEmpty(cam.name) && cam.name.IndexOf("cameraplus", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsCamera2Camera(Camera cam)
        {
            if (cam == null) return false;

            if (!string.IsNullOrEmpty(cam.name) && cam.name.IndexOf("Cam2", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            var p = cam.transform.parent;
            while (p != null)
            {
                if (p.name.StartsWith("Cam2_", StringComparison.OrdinalIgnoreCase) ||
                    p.name.IndexOf("Cam2", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
                p = p.parent;
            }

            return false;
        }

        private Camera TryFindCamera2ViaSDK()
        {
            try
            {
                var camManagerType = Type.GetType("Camera2.Managers.CamManager, Camera2");
                if (camManagerType == null)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (string.Equals(asm.GetName().Name, "Camera2", StringComparison.OrdinalIgnoreCase))
                        {
                            camManagerType = asm.GetType("Camera2.Managers.CamManager");
                            if (camManagerType != null) break;
                        }
                    }
                }

                if (camManagerType != null)
                {
                    var camsProp = camManagerType.GetProperty("cams", BindingFlags.Public | BindingFlags.Static);
                    if (camsProp != null)
                    {
                        var camsDict = camsProp.GetValue(null) as System.Collections.IDictionary;
                        if (camsDict != null && camsDict.Count > 0)
                        {
                            Camera fallbackCam = null;
                            foreach (System.Collections.DictionaryEntry entry in camsDict)
                            {
                                var cam2Obj = entry.Value;
                                if (cam2Obj == null) continue;

                                var cam2Type = cam2Obj.GetType();
                                var goProp = cam2Type.GetProperty("gameObject", BindingFlags.Public | BindingFlags.Instance);
                                var go = goProp?.GetValue(cam2Obj) as GameObject;
                                if (go == null || !go.activeInHierarchy) continue;

                                var uCamProp = cam2Type.GetProperty("UCamera", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                var uCam = uCamProp?.GetValue(cam2Obj) as Camera;
                                if (!IsUsableSpectatorCamera(uCam)) continue;

                                var settingsProp = cam2Type.GetProperty("settings", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                var settings = settingsProp?.GetValue(cam2Obj);
                                if (settings != null)
                                {
                                    var typeProp = settings.GetType().GetProperty("type", BindingFlags.Public | BindingFlags.Instance);
                                    var typeVal = typeProp?.GetValue(settings);
                                    if (typeVal != null && typeVal.ToString().Equals("Positionable", StringComparison.OrdinalIgnoreCase))
                                    {
                                        return uCam;
                                    }
                                }

                                if (fallbackCam == null)
                                    fallbackCam = uCam;
                            }
                            if (fallbackCam != null)
                                return fallbackCam;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (Plugin.Config.DebugLog)
                    Plugin.Log.Debug($"Camera2 SDK probe error: {ex.Message}");
            }

            return null;
        }

        private bool TryFindTrackedCamera(bool force)
        {
            if (!force && IsUsableSpectatorCamera(_trackedCamera))
                return true;

            _trackedCamera = null;
            string detectedType = null;

            var allCameras = Resources.FindObjectsOfTypeAll<Camera>();

            // 1. Try CameraPlus main camera
            _trackedCamera = allCameras.FirstOrDefault(c =>
                IsUsableSpectatorCamera(c) &&
                string.Equals(c.name, CameraPlusMainCameraName, StringComparison.OrdinalIgnoreCase));

            if (_trackedCamera != null)
            {
                detectedType = "CameraPlus (cameraplus.json)";
            }
            else
            {
                // 2. Try any active CameraPlus camera with desktop target texture
                _trackedCamera = allCameras.FirstOrDefault(c =>
                    IsUsableSpectatorCamera(c) &&
                    c.targetTexture != null &&
                    IsCameraPlusCamera(c));

                if (_trackedCamera != null)
                {
                    detectedType = "CameraPlus";
                }
            }

            // 3. If CameraPlus not found, try Camera2 via SDK / Reflection
            if (_trackedCamera == null)
            {
                _trackedCamera = TryFindCamera2ViaSDK();
                if (_trackedCamera != null)
                {
                    detectedType = "Camera2 (SDK)";
                }
            }

            // 4. If still not found, try Camera2 via Hierarchy scan
            if (_trackedCamera == null)
            {
                _trackedCamera = allCameras.FirstOrDefault(c =>
                    IsUsableSpectatorCamera(c) &&
                    c.targetTexture != null &&
                    IsCamera2Camera(c));

                if (_trackedCamera != null)
                {
                    detectedType = "Camera2 (Hierarchy)";
                }
            }

            // 5. Fallback: Any active non-stereo third-person camera that outputs to a RenderTexture
            if (_trackedCamera == null)
            {
                _trackedCamera = allCameras.FirstOrDefault(c =>
                    IsUsableSpectatorCamera(c) &&
                    c.targetTexture != null &&
                    c != Camera.main &&
                    !string.Equals(c.name, "MainCamera", StringComparison.OrdinalIgnoreCase));

                if (_trackedCamera != null)
                {
                    detectedType = "Generic Third-Person Camera";
                }
            }

            if (_trackedCamera == null)
            {
                SyncCameraCullingMasks(allCameras, null);
                if (force || Plugin.Config.DebugLog)
                    Plugin.Log.Warn("No spectator camera (CameraPlus or Camera2) found yet.");
                return false;
            }

            SyncCameraCullingMasks(allCameras, _trackedCamera);

            Plugin.Log.Info($"Tracked camera attached: '{_trackedCamera.name}' ({detectedType}, FOV={_trackedCamera.fieldOfView:F1}, Aspect={_trackedCamera.aspect:F3})");
            return true;
        }

        private static bool IsUsableSpectatorCamera(Camera cam)
        {
            if (cam == null || !cam.gameObject.activeInHierarchy ||
                cam.stereoTargetEye != StereoTargetEyeMask.None)
                return false;

            // Camera2 turns UCamera.enabled off between rendered frames for its FPS
            // limiter. The owning Cam2 behaviour, rather than UCamera, controls
            // whether this camera participates in the current scene.
            if (IsCamera2Camera(cam))
            {
                var ownerType = _camera2ControllerType ??
                    (_camera2ControllerType = Type.GetType("Camera2.Behaviours.Cam2, Camera2"));
                var owner = ownerType != null ? cam.GetComponentInParent(ownerType) as Behaviour : null;
                if (owner != null)
                    return owner.isActiveAndEnabled;
            }

            return cam.enabled;
        }

        private void SyncCameraCullingMasks(Camera[] cameras, Camera primaryCam)
        {
            if (cameras == null) return;
            foreach (var cam in cameras)
            {
                SyncCameraCullingMask(cam, primaryCam);
            }
        }

        private void SyncCameraCullingMask(Camera cam, Camera primaryCam)
        {
            if (cam == null) return;
            var bit = 1 << _cameraLayer;
            if (!_originalLayerVisibility.ContainsKey(cam))
                _originalLayerVisibility.Add(cam, (cam.cullingMask & bit) != 0);

            // The shared quads match only the tracked camera's pose and projection.
            cam.cullingMask = cam == primaryCam ? cam.cullingMask | bit : cam.cullingMask & ~bit;
        }

        private void RestoreCameraCullingMasks()
        {
            var bit = 1 << _cameraLayer;
            foreach (var entry in _originalLayerVisibility)
            {
                if (entry.Key == null) continue;
                // Restore only our bit, preserving other mods' changes to the mask.
                entry.Key.cullingMask = entry.Value
                    ? entry.Key.cullingMask | bit
                    : entry.Key.cullingMask & ~bit;
            }
            _originalLayerVisibility.Clear();
        }

        private void SetSongFolder(string folder)
        {
            _songFolder = folder;
            _songConfig = null;

            if (string.IsNullOrEmpty(folder))
            {
                _front.Load(null);
                _back.Load(null);
                return;
            }

            var songConfigPath = Path.Combine(folder, "videolayer.json");
            if (File.Exists(songConfigPath))
            {
                try
                {
                    var json = File.ReadAllText(songConfigPath);
                    _songConfig = JsonUtility.FromJson<SongVideoLayerConfig>(json);
                    Plugin.Log.Info($"Loaded per-song config from {songConfigPath}");
                }
                catch (Exception ex)
                {
                    Plugin.Log.Warn($"Failed to load per-song config: {ex.Message}");
                }
            }

            var frontPath = Path.Combine(folder, FrontFileName);
            var backPath = Path.Combine(folder, BackFileName);

            _front.Load(File.Exists(frontPath) ? frontPath : null);
            _back.Load(File.Exists(backPath) ? backPath : null);

            // If per-song config specified custom blend mode, apply it
            if (_songConfig != null)
            {
                var fMode = _songConfig.frontBlendMode ?? _songConfig.FrontBlendMode;
                if (!string.IsNullOrEmpty(fMode))
                {
                    _front.ApplyShader(ResolveShader(fMode), Plugin.Config.FrontRenderQueue);
                }

                var bMode = _songConfig.backBlendMode ?? _songConfig.BackBlendMode;
                if (!string.IsNullOrEmpty(bMode))
                {
                    _back.ApplyShader(ResolveShader(bMode), Plugin.Config.BackRenderQueue);
                }
            }

            Plugin.Log.Info($"VideoLayer map folder: {folder}");
            Plugin.Log.Info($"  front.mp4: {(File.Exists(frontPath) ? "found" : "none")}");
            Plugin.Log.Info($"  back.mp4 : {(File.Exists(backPath) ? "found" : "none")}");
            Plugin.Log.Info($"  Effective distances: Front={GetCurrentFrontDistance():F2}m, BackOffset={GetCurrentBackOffset():F2}m");
        }

        private string FindCustomSongFolder(BeatmapLevel level)
        {
            if (level == null)
                return null;

            var levelId = level.levelID;
            Plugin.Log.Info($"Finding custom song folder for levelID: {levelId}");

            try
            {
                if (SongCore.Loader.CustomLevels != null)
                {
                    foreach (var pair in SongCore.Loader.CustomLevels)
                    {
                        if (pair.Value != null && string.Equals(pair.Value.levelID, levelId, StringComparison.OrdinalIgnoreCase))
                        {
                            Plugin.Log.Info($"Found custom song folder via CustomLevels: {pair.Key}");
                            return pair.Key;
                        }
                    }
                }

                if (SongCore.Loader.CustomWIPLevels != null)
                {
                    foreach (var pair in SongCore.Loader.CustomWIPLevels)
                    {
                        if (pair.Value != null && string.Equals(pair.Value.levelID, levelId, StringComparison.OrdinalIgnoreCase))
                        {
                            Plugin.Log.Info($"Found custom song folder via CustomWIPLevels: {pair.Key}");
                            return pair.Key;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"Could not resolve custom song folder via SongCore: {ex.Message}");
            }

            try
            {
                var customLevelsPath = Path.Combine(Application.dataPath, "CustomLevels");
                if (Directory.Exists(customLevelsPath))
                {
                    var hash = levelId != null && levelId.StartsWith("custom_level_", StringComparison.OrdinalIgnoreCase)
                        ? levelId.Substring("custom_level_".Length)
                        : levelId;

                    foreach (var dir in Directory.GetDirectories(customLevelsPath))
                    {
                        var dirName = Path.GetFileName(dir);
                        if (!string.IsNullOrEmpty(hash) && dirName.IndexOf(hash, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Plugin.Log.Info($"Found custom song folder via directory hash match: {dir}");
                            return dir;
                        }
                        if (!string.IsNullOrEmpty(level.songName) && dirName.IndexOf(level.songName, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            if (File.Exists(Path.Combine(dir, FrontFileName)) || File.Exists(Path.Combine(dir, BackFileName)))
                            {
                                Plugin.Log.Info($"Found custom song folder via song name and video existence: {dir}");
                                return dir;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"Fallback directory scan failed: {ex.Message}");
            }

            Plugin.Log.Warn($"Could not find song folder for levelID: {levelId}");
            return null;
        }

        private static Shader ResolveShader(string blendMode)
        {
            var mode = (blendMode ?? "ChromaKey").Trim();
            if (mode.Equals("GreenKey", StringComparison.OrdinalIgnoreCase))
                return VideoShaders.Find("VideoLayer/GreenKey");
            if (mode.Equals("Opaque", StringComparison.OrdinalIgnoreCase))
                return VideoShaders.Find("VideoLayer/Opaque");
            if (mode.Equals("PureAdditive", StringComparison.OrdinalIgnoreCase) || mode.Equals("AdditiveGlow", StringComparison.OrdinalIgnoreCase))
                return VideoShaders.Find("VideoLayer/PureAdditive");
            if (!mode.Equals("ChromaKey", StringComparison.OrdinalIgnoreCase))
                Plugin.Log.Warn($"Unknown blend mode '{mode}'; using ChromaKey.");
            return VideoShaders.Find("VideoLayer/ChromaKey");
        }
        private void HandleDebugKeys()
        {
            if (!Plugin.Config.DebugLog)
                return;

            if (Input.GetKeyDown(KeyCode.F9))
            {
                if (_back.Quad != null)
                {
                    bool next = !_back.Visible;
                    _back.SetActive(next);
                    Plugin.Log.Info($"[F9] Back Quad visibility toggled: {next}");
                }
            }

            if (Input.GetKeyDown(KeyCode.F8))
            {
                _currentShaderIndex = (_currentShaderIndex + 1) % DebugShaderNames.Length;
                var shaderName = DebugShaderNames[_currentShaderIndex];
                var shader = VideoShaders.Find(shaderName);
                if (shader != null)
                {
                    Plugin.Log.Info($"[F8] >>> SWITCHED TO SHADER [{_currentShaderIndex}/{DebugShaderNames.Length - 1}]: {shader.name} <<<");
                    _front.ApplyShader(shader, Plugin.Config.FrontRenderQueue);
                    _back.ApplyShader(shader, Plugin.Config.BackRenderQueue);
                }
            }
        }

        private static void DumpAllGameShaders()
        {
            try
            {
                var all = Resources.FindObjectsOfTypeAll<Shader>();
                var list = all.Where(s => s != null && !string.IsNullOrEmpty(s.name))
                              .Select(s => s.name)
                              .Distinct()
                              .OrderBy(n => n)
                              .ToList();
                var outPath = Path.Combine(Application.dataPath, "..", "UserData", "VideoLayer_Shaders.txt");
                File.WriteAllLines(outPath, list);
                Plugin.Log.Info($"Dumped {list.Count} game shaders to {outPath}");
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"Failed to dump shaders: {ex.Message}");
            }
        }
    }

    [Serializable]
    internal class SongVideoLayerConfig
    {
        public float backOffsetMeters = float.NaN;
        public float BackOffsetMeters = float.NaN;
        public float frontDistanceMeters = float.NaN;
        public float FrontDistanceMeters = float.NaN;
        public string frontBlendMode;
        public string FrontBlendMode;
        public string backBlendMode;
        public string BackBlendMode;
    }
}
