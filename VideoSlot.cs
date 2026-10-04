using System;
using System.IO;
using UnityEngine;
using UnityEngine.Video;

namespace VideoLayer
{
    /// <summary>
    /// Encapsulates video playback, texture rendering, material binding, and quad geometry for a single video layer (front or back).
    /// </summary>
    internal sealed class VideoSlot : IDisposable
    {
        public string Name { get; }
        public string Path { get; private set; }
        public bool Ready { get; private set; }
        public bool Started { get; private set; }
        public GameObject Quad { get; private set; }
        public Material Material { get; private set; }

        public uint VideoWidth { get; private set; }
        public uint VideoHeight { get; private set; }

        private GameObject _playerObject;
        private VideoPlayer _player;
        private RenderTexture _renderTexture;
        private MeshRenderer _renderer;
        private DateTime _lastWriteUtc;
        private int _cameraLayer;
        private bool _visible = true;
        private bool _geometryReady;
        private bool _seekPending;
        private bool _seekCompleted;
        private bool _forceSeek;
        private bool _reachedEnd;
        private bool _haveSongTime;
        private double _lastSongTime;

        public bool Visible => _visible;
        private int _initialRenderWidth = 1920;
        private int _initialRenderHeight = 1080;

        public VideoSlot(string name)
        {
            Name = name;
        }

        public void Initialize(Transform parent, int cameraLayer, int renderWidth, int renderHeight, Shader defaultShader, int renderQueue)
        {
            _cameraLayer = Mathf.Clamp(cameraLayer, 0, 31);
            _initialRenderWidth = Mathf.Max(16, renderWidth);
            _initialRenderHeight = Mathf.Max(16, renderHeight);
            VideoWidth = (uint)_initialRenderWidth;
            VideoHeight = (uint)_initialRenderHeight;

            // Player object & VideoPlayer setup
            _playerObject = new GameObject($"VideoLayer.{Name}.Player");
            _playerObject.transform.SetParent(parent, false);

            _player = _playerObject.AddComponent<VideoPlayer>();
            _player.source = VideoSource.Url;
            _player.playOnAwake = false;
            _player.isLooping = false;
            _player.skipOnDrop = true;
            _player.waitForFirstFrame = true;
            _player.audioOutputMode = VideoAudioOutputMode.None;
            _player.renderMode = VideoRenderMode.RenderTexture;
            _player.aspectRatio = VideoAspectRatio.Stretch;
            _player.prepareCompleted += OnPrepareCompleted;
            _player.errorReceived += OnVideoError;
            _player.seekCompleted += OnSeekCompleted;
            _player.frameReady += OnFrameReady;
            _player.loopPointReached += OnVideoEnded;
            _player.sendFrameReadyEvents = true;

            // Render texture setup
            CreateRenderTexture(_initialRenderWidth, _initialRenderHeight);

            // Quad geometry setup
            Quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Quad.name = $"VideoLayer.{Name}.Quad";
            Quad.transform.SetParent(parent, false);
            Quad.layer = _cameraLayer;
            var col = Quad.GetComponent<Collider>();
            if (col) UnityEngine.Object.Destroy(col);

            _renderer = Quad.GetComponent<MeshRenderer>();
            ApplyShader(defaultShader, renderQueue);
            Quad.SetActive(false);
        }

        private void CreateRenderTexture(int width, int height)
        {
            var maxWidth = Mathf.Max(640, Plugin.Config != null ? Plugin.Config.MaxRenderWidth : 1920);
            var maxHeight = Mathf.Max(360, Plugin.Config != null ? Plugin.Config.MaxRenderHeight : 1080);

            // Downscale safely while preserving aspect ratio if exceeding maximum allowed resolution
            if (width > maxWidth || height > maxHeight)
            {
                float scale = Mathf.Min((float)maxWidth / width, (float)maxHeight / height);
                width = Mathf.RoundToInt(width * scale);
                height = Mathf.RoundToInt(height * scale);
            }

            width = Mathf.Clamp(width, 16, 4096);
            height = Mathf.Clamp(height, 16, 4096);

            if (_renderTexture != null && _renderTexture.width == width && _renderTexture.height == height && _renderTexture.IsCreated())
                return;

            if (_renderTexture != null)
            {
                _renderTexture.Release();
                UnityEngine.Object.Destroy(_renderTexture);
            }

            _renderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = $"VideoLayer.{Name}.RT",
                useMipMap = false,
                autoGenerateMips = false,
                anisoLevel = 1
            };
            _renderTexture.Create();

            if (_player != null)
                _player.targetTexture = _renderTexture;

            BindTextureToMaterial();
        }

        private void BindTextureToMaterial()
        {
            if (Material == null || _renderTexture == null)
                return;

            if (Material.HasProperty("_MainTex")) Material.SetTexture("_MainTex", _renderTexture);
            if (Material.HasProperty("_Texture")) Material.SetTexture("_Texture", _renderTexture);
            if (Material.HasProperty("_BaseMap")) Material.SetTexture("_BaseMap", _renderTexture);
            Material.mainTexture = _renderTexture;
        }

        public void ApplyShader(Shader shader, int renderQueue)
        {
            if (shader == null || _renderer == null)
                return;

            var mat = new Material(shader)
            {
                name = $"VideoLayer.{Name}.{shader.name}.Mat",
                renderQueue = renderQueue
            };

            if (mat.HasProperty("_KeyColor")) mat.SetColor("_KeyColor", Color.black);
            if (mat.HasProperty("_ColorKey")) mat.SetColor("_ColorKey", Color.black);
            if (mat.HasProperty("_ChromaColor")) mat.SetColor("_ChromaColor", Color.black);
            if (mat.HasProperty("_Threshold")) mat.SetFloat("_Threshold", 0.05f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.05f);
            if (mat.HasProperty("_Range")) mat.SetFloat("_Range", 0.1f);

            if (Material != null) UnityEngine.Object.Destroy(Material);
            Material = mat;
            _renderer.sharedMaterial = mat;
            BindTextureToMaterial();

            Plugin.Log.Info($"[{Name}] Applied shader '{shader.name}' (queue={renderQueue})");
        }

        public void Load(string filePath)
        {
            var writeTime = !string.IsNullOrEmpty(filePath) && File.Exists(filePath)
                ? File.GetLastWriteTimeUtc(filePath)
                : DateTime.MinValue;

            if (string.Equals(Path, filePath, StringComparison.OrdinalIgnoreCase) &&
                _lastWriteUtc == writeTime &&
                (string.IsNullOrEmpty(filePath) || Ready || (_player != null && _player.isPrepared)))
            {
                return;
            }

            Path = filePath;
            _lastWriteUtc = writeTime;
            Ready = false;
            Started = false;
            _geometryReady = false;
            ResetPlaybackState();

            if (_player == null)
                return;

            _player.Stop();
            _player.url = string.Empty;
            if (Quad != null)
                Quad.SetActive(false);

            if (string.IsNullOrEmpty(filePath))
                return;

            try
            {
                var fullPath = System.IO.Path.GetFullPath(filePath).Replace('\\', '/');
                _player.url = fullPath;
                Plugin.Log.Info($"Preparing {Name} video player with url: '{fullPath}'");
                _player.Prepare();

                if (_player.isPrepared)
                {
                    OnPrepareCompleted(_player);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Failed to prepare {Name} video '{filePath}': {ex}");
            }
        }

        public void Sync(double songTime, bool songAdvancing, float songRate, float syncThreshold)
        {
            if (!Ready || string.IsNullOrEmpty(Path) || _player == null)
                return;

            songTime = Math.Max(0.0, songTime);
            var rewound = _haveSongTime && songTime < _lastSongTime - 0.001;
            _lastSongTime = songTime;
            _haveSongTime = true;
            var pastEnd = IsPastEnd(songTime);
            if (_reachedEnd && rewound && !pastEnd)
            {
                _reachedEnd = false;
                _forceSeek = true;
            }
            if (pastEnd)
                _reachedEnd = true;

            RefreshVisibility();
            if (_reachedEnd)
            {
                Pause();
                return;
            }

            if (!Started)
            {
                _forceSeek = true;
                Started = true;
            }

            if (_player.canSetPlaybackSpeed)
                _player.playbackSpeed = Mathf.Clamp(songRate, 0.05f, 4f);

            // Re-evaluate the latest song time each update; never queue stale targets
            // while Unity is still seeking or preparing the resulting frame.
            if (_forceSeek || Math.Abs(_player.time - songTime) > Math.Max(0.001f, syncThreshold))
                RequestSeek(songTime);

            if (songAdvancing)
            {
                if (!_player.isPlaying)
                {
                    Plugin.Log.Info($"Playing {Name} at songTime={songTime:F2} (Quad active={Quad?.activeSelf}, layer={Quad?.layer})");
                    _player.Play();
                }
            }
            else if (_player.isPlaying)
            {
                _player.Pause();
            }
        }

        public void CheckPrepared()
        {
            if (!Ready && _player != null && _player.isPrepared && !string.IsNullOrEmpty(Path))
            {
                OnPrepareCompleted(_player);
            }
        }

        public void Seek(double songTime)
        {
            if (!Ready || _player == null || !_player.canSetTime)
                return;

            _forceSeek = true;
            if (!IsPastEnd(Math.Max(0.0, songTime)))
                _reachedEnd = false;
            // Sync supplies the latest target, including any changes while seeking.
        }

        private bool IsPastEnd(double songTime)
        {
            var length = _player.length;
            return length > 0.0 && !double.IsNaN(length) && !double.IsInfinity(length) && songTime >= length;
        }

        private void RequestSeek(double songTime)
        {
            if (!_player.canSetTime || _seekPending)
                return;

            _forceSeek = false;
            if (Math.Abs(_player.time - songTime) <= 0.001)
                return;

            _seekCompleted = false;
            _seekPending = true;
            try
            {
                _player.time = songTime;
            }
            catch
            {
                _seekPending = false;
                throw;
            }
        }

        private void OnSeekCompleted(VideoPlayer source)
        {
            if (source == _player && Ready && _seekPending)
                _seekCompleted = true;
        }

        private void OnFrameReady(VideoPlayer source, long frameIndex)
        {
            // seekCompleted precedes presentation of the requested frame. Waiting
            // for frameReady avoids correcting against the previous frame's time.
            if (source == _player && Ready && _seekPending && _seekCompleted)
            {
                _seekPending = false;
                _seekCompleted = false;
            }
        }

        private void OnVideoEnded(VideoPlayer source)
        {
            if (source != _player || !Ready)
                return;

            _reachedEnd = true;
            Pause();
            RefreshVisibility();
        }

        private void ResetPlaybackState()
        {
            _seekPending = false;
            _seekCompleted = false;
            _forceSeek = false;
            _reachedEnd = false;
            _haveSongTime = false;
            _lastSongTime = 0.0;
        }

        public void Pause()
        {
            if (_player != null && _player.isPlaying)
                _player.Pause();
        }

        public void StopAndHide(Transform defaultParent)
        {
            Ready = false;
            Started = false;
            _geometryReady = false;
            _visible = true;
            ResetPlaybackState();

            if (_player != null)
            {
                try
                {
                    _player.Stop();
                    _player.url = string.Empty;
                }
                catch { }
            }

            if (Quad != null)
            {
                Quad.SetActive(false);
                if (defaultParent != null && Quad.transform.parent != defaultParent)
                    Quad.transform.SetParent(defaultParent, false);
            }

            // Clear the render texture to fully transparent so no lingering frame remains
            if (_renderTexture != null && _renderTexture.IsCreated())
            {
                var prevActive = RenderTexture.active;
                try
                {
                    RenderTexture.active = _renderTexture;
                    GL.Clear(true, true, Color.clear);
                }
                catch { }
                finally
                {
                    RenderTexture.active = prevActive;
                }
            }

            Plugin.Log.Info($"[{Name}] Completely stopped and hidden.");
        }

        public void SetActive(bool active)
        {
            _visible = active;
            RefreshVisibility();
        }

        private void RefreshVisibility()
        {
            if (Quad != null)
                Quad.SetActive(_visible && _geometryReady && Ready && !_reachedEnd && !string.IsNullOrEmpty(Path));
        }

        public void HideGeometry()
        {
            _geometryReady = false;
            RefreshVisibility();
        }

        public void SetCameraLayer(int layer)
        {
            _cameraLayer = Mathf.Clamp(layer, 0, 31);
            if (Quad != null)
                Quad.layer = _cameraLayer;
        }

        public float GetVideoAspect()
        {
            if (VideoWidth > 0 && VideoHeight > 0)
                return (float)VideoWidth / (float)VideoHeight;

            if (_renderTexture != null && _renderTexture.width > 0 && _renderTexture.height > 0)
                return (float)_renderTexture.width / (float)_renderTexture.height;

            return 16f / 9f;
        }

        public void UpdateGeometry(Camera camera, float depth, float overscan)
        {
            if (!Ready || Quad == null || camera == null)
                return;

            if (Quad.layer != _cameraLayer)
                Quad.layer = _cameraLayer;

            // Check if video dimensions became available or changed
            if (_player != null && _player.isPrepared)
            {
                var curW = _player.width;
                var curH = _player.height;
                if (curW > 0 && curH > 0 && (curW != VideoWidth || curH != VideoHeight))
                {
                    VideoWidth = curW;
                    VideoHeight = curH;
                    CreateRenderTexture((int)curW, (int)curH);
                }
            }

            float camHeight;
            if (camera.orthographic)
            {
                camHeight = camera.orthographicSize * 2f;
            }
            else
            {
                camHeight = 2f * depth * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            }

            float camAspect = camera.aspect;
            float camWidth = camHeight * camAspect;

            float videoAspect = GetVideoAspect();

            float quadWidth;
            float quadHeight;

            // Fit Inside (preserve aspect ratio, letterbox/pillarbox centered on camera)
            if (camAspect > videoAspect)
            {
                // Camera is wider than video: fit to camera height, centered horizontally
                quadHeight = camHeight;
                quadWidth = camHeight * videoAspect;
            }
            else
            {
                // Camera is taller than video: fit to camera width, centered vertically
                quadWidth = camWidth;
                quadHeight = camWidth / videoAspect;
            }

            var scaleFactor = Mathf.Max(1f, overscan);
            // Keep ownership under the persistent controller so replacing a camera
            // cannot destroy the video surface along with the camera's children.
            Quad.transform.SetPositionAndRotation(
                camera.transform.position + camera.transform.forward * depth,
                camera.transform.rotation);
            Quad.transform.localScale = new Vector3(quadWidth * scaleFactor, quadHeight * scaleFactor, 1f);

            _geometryReady = true;
            RefreshVisibility();
        }

        private void OnPrepareCompleted(VideoPlayer source)
        {
            Ready = true;

            var vWidth = source.width;
            var vHeight = source.height;

            if (vWidth > 0 && vHeight > 0)
            {
                VideoWidth = vWidth;
                VideoHeight = vHeight;
                CreateRenderTexture((int)vWidth, (int)vHeight);
            }
            else
            {
                BindTextureToMaterial();
            }

            Plugin.Log.Info($"{Name} prepared successfully: {Path} ({VideoWidth}x{VideoHeight}, Aspect={GetVideoAspect():F3})");
        }

        private void OnVideoError(VideoPlayer source, string message)
        {
            ResetPlaybackState();
            Plugin.Log.Error($"{Name} playback error: {message}, url: '{source?.url}'");
        }

        public void Dispose()
        {
            if (_player != null)
            {
                _player.prepareCompleted -= OnPrepareCompleted;
                _player.errorReceived -= OnVideoError;
                _player.seekCompleted -= OnSeekCompleted;
                _player.frameReady -= OnFrameReady;
                _player.loopPointReached -= OnVideoEnded;
                _player.Stop();
            }

            if (Material != null) UnityEngine.Object.Destroy(Material);
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                UnityEngine.Object.Destroy(_renderTexture);
            }
            if (Quad != null) UnityEngine.Object.Destroy(Quad);
            if (_playerObject != null) UnityEngine.Object.Destroy(_playerObject);
        }
    }
}
