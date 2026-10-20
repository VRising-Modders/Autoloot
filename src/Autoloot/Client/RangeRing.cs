// SPDX-License-Identifier: AGPL-3.0-only
using System;
using Autoloot.Config;
using Progenitor.Ecs;
using ProjectM;
using ProjectM.Network;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace Autoloot.Client
{
    /// <summary>Client-only ghost-fire boundary, projected onto streamed gameplay floors.</summary>
    internal sealed class RangeRing
    {
        const int Samples = 128;
        const int VerticesPerFlame = 12;
        const float Lift = 0.10f;
        const double ShowAfterChangeSeconds = 20;
        static readonly Color Active = new(0.28f, 1.25f, 1.05f, 1f);
        static readonly Color Off = new(0.24f, 0.28f, 0.30f, 1f);

        readonly AutolootContext _ctx;
        readonly ServerConfig _server;
        readonly PlayerConfig _player;
        GameObject? _root;
        Mesh? _mesh;
        Material? _material;
        Texture2D? _texture;
        readonly Vector3[] _floor = new Vector3[Samples];
        readonly float[] _shownHeight = new float[Samples];
        readonly bool[] _valid = new bool[Samples];
        readonly Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3> _vertices = new(Samples * VerticesPerFlame);
        Vector3 _sampleCenter;
        float _lastSample, _lastAnimate;
        bool _visible;
        Entity _character = Entity.Null;
        float _drawnRadius = -1;
        bool _drawnOn;
        double _showUntil;
        float _nextLookup;
        bool _broken;
        float _joinedAt = -1;
        const float JoinQuietSeconds = 10;

        internal RangeRing(AutolootContext ctx, ServerConfig server, PlayerConfig player)
        {
            _ctx = ctx;
            _server = server;
            _player = player;
        }

        internal void Attach()
        {
            _server.RadiusChanged += OnRadiusChanged;
            _ctx.Track(new OnDispose(() => _server.RadiusChanged -= OnRadiusChanged));
            _ctx.Track(new OnDispose(Destroy));
            _ctx.Track(_ctx.Systems.Add("autoloot.range", _ => Update()));
        }

        /// <summary>
        /// A range change while joined (an admin's edit) shows the ring to everyone online. The server's
        /// value arriving at join also raises a change; that one is skipped.
        /// </summary>
        void OnRadiusChanged()
        {
            if (_ctx.Handshake.ServerPart != Progenitor.Plugins.CounterpartPresence.Running) { _joinedAt = -1; return; }
            if (_joinedAt < 0 || Time.realtimeSinceStartup - _joinedAt < JoinQuietSeconds) return;
            Flash();
        }

        /// <summary>Show for a while (the settings page's Show button, or a range change).</summary>
        internal void Flash() => _showUntil = Time.realtimeSinceStartup + ShowAfterChangeSeconds;

        void Update()
        {
            if (_broken) return;
            try
            {
                var joined = _ctx.Handshake.ServerPart == Progenitor.Plugins.CounterpartPresence.Running;
                if (!joined) _joinedAt = -1;
                else if (_joinedAt < 0) _joinedAt = Time.realtimeSinceStartup;
                var show = _player.ShowRange.Value || Time.realtimeSinceStartup < _showUntil;
                if (!joined || !show || !TryCharacter(out var at))
                {
                    if (_root != null && _visible) _root.SetActive(false);
                    _visible = false;
                    return;
                }
                if (!Ensure()) return;
                var now = Time.realtimeSinceStartup;
                var center = new Vector3(at.x, at.y, at.z);
                var radius = Math.Clamp(_server.Radius.Value, Engine.ServerRules.MinRadius, Engine.ServerRules.MaxRadius);
                var on = _server.Enabled.Value && _player.MyOn;
                bool moved = (center - _sampleCenter).sqrMagnitude > 0.0025f;
                bool reset = !_visible || radius != _drawnRadius || (center - _sampleCenter).sqrMagnitude > 25;
                if (reset || now - _lastSample >= (moved ? 0.125f : 0.5f))
                {
                    SampleGround(center, radius, reset);
                    // Stationary previews still refresh streamed tiles. Moving previews sample at 8 Hz.
                    _lastSample = now;
                    _sampleCenter = center;
                    _drawnRadius = radius;
                }
                if (!_visible || on != _drawnOn)
                {
                    var color = on ? Active : Off;
                    if (_material!.HasProperty("_UnlitColor")) _material.SetColor("_UnlitColor", color);
                    if (_material.HasProperty("_Color")) _material.SetColor("_Color", color);
                    _drawnOn = on;
                }
                // Horizontal tracking must not wait for terrain probes: that made the whole
                // boundary jump eight times a second. Vertices are local in X/Z, world-height in Y.
                _root!.transform.position = new Vector3(center.x, 0, center.z);
                float dt = _visible ? Math.Clamp(now - _lastAnimate, 0, 0.1f) : 0;
                Animate(now, radius, on, dt);
                _lastAnimate = now;
                if (!_visible) _root!.SetActive(true);
                _visible = true;
            }
            catch (Exception e)
            {
                _broken = true;
                _ctx.Log.LogWarning("Autoloot: the range ring cannot be drawn here: " + e.Message);
                Destroy();
            }
        }

        bool TryCharacter(out Unity.Mathematics.float3 at)
        {
            at = default;
            var world = _ctx.World;
            if (world == null || !world.IsCreated) return false;
            var em = world.EntityManager;
            // After a rejoin the old id can name another entity in the new world.
            if (_character == Entity.Null || !em.Exists(_character) || !_character.Has<LocalCharacter>(em))
            {
                // Looked up at most twice a second (on the title screen there is none).
                var now = Time.realtimeSinceStartup;
                if (now < _nextLookup) return false;
                _nextLookup = now + 0.5f;
                var locals = Query.Entities(em, includeDisabled: false, includeSystems: false,
                    ComponentIndex.Of<LocalCharacter>(ComponentType.AccessMode.ReadOnly));
                _character = locals.Count > 0 ? locals[0] : Entity.Null;
                if (_character == Entity.Null) return false;
            }
            if (!_character.TryRead<Translation>(em, out var t)) return false;
            at = t.Value;
            return true;
        }

        bool Ensure()
        {
            if (_root != null && _mesh != null) return true;
            Destroy();
            _material = MakeMaterial(Active);
            if (_material == null) throw new InvalidOperationException("No supported ghost-fire shader");
            // RGB falloff matters: HDRP additive unlit does not multiply RGB by alpha.
            _texture = new Texture2D(32, 64, TextureFormat.RGBA32, false);
            _texture.name = "autoloot-ghost-fire";
            _texture.wrapMode = TextureWrapMode.Clamp;
            _texture.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < 64; y++)
            for (int x = 0; x < 32; x++)
            {
                float u = x / 31f, v = y / 63f;
                float edge = Mathf.Pow(Mathf.Sin(u * Mathf.PI), 3);
                float fade = Mathf.Sin(v * Mathf.PI) * (1 - v);
                float light = edge * fade;
                _texture.SetPixel(x, y, new Color(light, light, light, light));
            }
            _texture.Apply(false, true);
            if (_material.HasProperty("_UnlitColorMap"))
            {
                _material.SetTexture("_UnlitColorMap", _texture);
                _material.EnableKeyword("_UNLIT_COLOR_MAP");
            }
            if (_material.HasProperty("_MainTex")) _material.SetTexture("_MainTex", _texture);
            _root = new GameObject("autoloot-ghost-range");
            _root.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(_root);
            _mesh = new Mesh { name = "autoloot-ghost-range" };
            _mesh.MarkDynamic();
            var uv = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector2>(Samples * VerticesPerFlame);
            var triangles = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int>(Samples * 24);
            int index = 0;
            for (int i = 0; i < Samples; i++)
            {
                int start = i * VerticesPerFlame;
                for (int j = 0; j < 4; j++)
                {
                    uv[start + j * 2] = new Vector2(0, j / 3f);
                    uv[start + j * 2 + 1] = new Vector2(1, j / 3f);
                }
                uv[start + 8] = new Vector2(0, 0);
                uv[start + 9] = new Vector2(1, 0);
                uv[start + 10] = new Vector2(0, 1);
                uv[start + 11] = new Vector2(1, 1);
                for (int j = 0; j < 4; j++)
                {
                    int a = start + (j == 3 ? 8 : j * 2);
                    triangles[index++] = a; triangles[index++] = a + 2; triangles[index++] = a + 1;
                    triangles[index++] = a + 1; triangles[index++] = a + 2; triangles[index++] = a + 3;
                }
            }
            _mesh.vertices = _vertices;
            _mesh.uv = uv;
            _mesh.triangles = triangles;
            _root.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var renderer = _root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _drawnRadius = -1;
            return true;
        }

        void SampleGround(Vector3 center, float radius, bool reset)
        {
            using var ground = Progenitor.Terrain.GroundProbe.TryOpen(_ctx.World, out _);
            // Walk out from the player's floor rather than probing from the sky (which selects roofs).
            // At most 128 * 15 cheap tile samples per refresh, never any physics raycasts.
            int steps = Math.Max(1, (int)Math.Ceiling(radius / 2));
            for (int i = 0; i < Samples; i++)
            {
                float angle = i * Mathf.PI * 2 / Samples;
                var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                float height = center.y;
                bool valid = ground != null;
                for (int step = 1; valid && step <= steps; step++)
                {
                    var point = center + direction * (radius * step / steps);
                    valid = ground!.TryGetHeight(point.x, height + 2.1f, point.z, out var next);
                    if (valid) height = next;
                }
                if (reset || !_valid[i]) _shownHeight[i] = height + Lift;
                _valid[i] = valid;
                _floor[i] = center + direction * radius;
                _floor[i].y = height + Lift;
            }
        }

        void Animate(float time, float radius, bool on, float dt)
        {
            var camera = Camera.main;
            Vector3 right = camera != null ? camera.transform.right : Vector3.right;
            right.y = 0;
            right.Normalize();
            float spacing = 2 * Mathf.PI * radius / Samples;
            float width = Mathf.Clamp(spacing * 1.35f, 0.12f, 0.85f);
            // Frame-rate independent easing for small floor changes. Large ledges snap rather
            // than dragging flames through empty space between separate floors.
            float blend = 1 - Mathf.Exp(-dt / 0.045f);
            for (int i = 0; i < Samples; i++)
            {
                int start = i * VerticesPerFlame;
                var floor = _floor[i];
                floor.x -= _sampleCenter.x;
                floor.z -= _sampleCenter.z;
                if (!_valid[i])
                {
                    // Missing tiles produce no geometry; never invent a floating fallback circle.
                    for (int j = 0; j < VerticesPerFlame; j++) _vertices[start + j] = floor;
                    continue;
                }
                _shownHeight[i] = Mathf.Abs(floor.y - _shownHeight[i]) > 1.5f
                    ? floor.y : Mathf.Lerp(_shownHeight[i], floor.y, blend);
                floor.y = _shownHeight[i];
                float seed = i * 2.399963f;
                float height = on ? 0.45f + 0.25f * (1 + Mathf.Sin(time * 2.6f + seed)) : 0.18f;
                for (int j = 0; j < 4; j++)
                {
                    float t = j / 3f;
                    float sway = on ? Mathf.Sin(time * 3 + seed + t * 3) * t * 0.18f : 0;
                    var middle = floor + Vector3.up * (height * t) + right * sway;
                    var half = right * (width * 0.5f * (1 - t * 0.92f));
                    _vertices[start + j * 2] = middle - half;
                    _vertices[start + j * 2 + 1] = middle + half;
                }
                float angle = i * Mathf.PI * 2 / Samples;
                var tangent = new Vector3(-Mathf.Sin(angle), 0, Mathf.Cos(angle)) * (spacing * 0.65f);
                var radial = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 0.16f;
                _vertices[start + 8] = floor - tangent - radial;
                _vertices[start + 9] = floor + tangent - radial;
                _vertices[start + 10] = floor - tangent + radial;
                _vertices[start + 11] = floor + tangent + radial;
            }
            _mesh!.vertices = _vertices;
            _mesh.RecalculateBounds();
        }

        static Material? MakeMaterial(Color color)
        {
            foreach (var name in new[] { "HDRP/Unlit", "Hidden/HDRP/Unlit", "Sprites/Default" })
            {
                Shader? shader = null;
                try { shader = Shader.Find(name); }
                catch (Exception) { }
                if (shader == null || !shader.isSupported) continue;
                var mat = new Material(shader) { name = "autoloot-range" };
                if (name.Contains("HDRP"))
                {
                    Set(mat, "_SurfaceType", 1);
                    Set(mat, "_BlendMode", 1);
                    Set(mat, "_SrcBlend", 1);
                    Set(mat, "_DstBlend", 1);
                    Set(mat, "_AlphaSrcBlend", 1);
                    Set(mat, "_AlphaDstBlend", 1);
                    Set(mat, "_ZWrite", 0);
                    Set(mat, "_TransparentZWrite", 0);
                    // Respect scene depth: flames behind buildings must stay hidden.
                    Set(mat, "_ZTestTransparent", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
                    Set(mat, "_ZTestDepthEqualForOpaque", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
                    // Flat on the ground: draw both faces, whichever way the camera looks.
                    Set(mat, "_CullMode", 0);
                    Set(mat, "_CullModeForward", 0);
                    Set(mat, "_DoubleSidedEnable", 1);
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.EnableKeyword("_BLENDMODE_ADD");
                    mat.DisableKeyword("_BLENDMODE_ALPHA");
                    mat.SetOverrideTag("RenderType", "Transparent");
                }
                // HDRP Unlit ignores vertex colours, so each colour is its own material.
                if (mat.HasProperty("_UnlitColor")) mat.SetColor("_UnlitColor", color);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
                mat.renderQueue = 3100;
                return mat;
            }
            return null;
        }

        static void Set(Material mat, string property, float value)
        {
            if (mat.HasProperty(property)) mat.SetFloat(property, value);
        }

        void Destroy()
        {
            try { if (_root != null) UnityEngine.Object.Destroy(_root); } catch (Exception) { }
            try { if (_mesh != null) UnityEngine.Object.Destroy(_mesh); } catch (Exception) { }
            try { if (_material != null) UnityEngine.Object.Destroy(_material); } catch (Exception) { }
            try { if (_texture != null) UnityEngine.Object.Destroy(_texture); } catch (Exception) { }
            _root = null;
            _mesh = null;
            _material = null;
            _texture = null;
            _visible = false;
            _drawnRadius = -1;
        }
    }
}
