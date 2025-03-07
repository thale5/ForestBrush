﻿using ColossalFramework;
using ColossalFramework.Math;
using ColossalFramework.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ForestBrush
{
    public class ForestTool : ToolBase
    {
        private static readonly string kCursorInfoNormalColor = "<color #87d3ff>";
        private static readonly string kCursorInfoCloseColorTag = "</color>";
        private float Angle = ToolsModifierControl.cameraController.m_currentAngle.x;
        private bool AxisChanged;
        private float MouseRayLength;
        private bool MouseLeftDown;
        private bool MouseRightDown;
        private bool MouseRayValid;
        private Ray MouseRay;
        private Vector3 MouseRayRight;
        private Vector3 CachedPosition;
        private Vector3 MousePosition;
        private Randomizer Randomizer;
        public TreeInfo Container = ForestBrush.Instance.Container;
        private float Size => Options.Size;
        private float Strength => Options.Strength;
        private float Density => Options.Density;
        private int TreeCount => UserMod.Settings.SelectedBrush.Trees.Count;
        private Brush.BrushOptions Options { get => UserMod.Settings.SelectedBrush.Options; set => UserMod.Settings.SelectedBrush.Options = value; }

        private bool ShiftDown => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        private bool AltDown => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        private bool CtrlDown => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                              || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);

        private bool Painting => MouseLeftDown && !CtrlDown && !ShiftDown && !MouseRightDown;
        private bool Deleting => MouseRightDown&& !AltDown && !CtrlDown && !MouseLeftDown;
        private bool DensityOrRotation => MouseRightDown && CtrlDown && !ShiftDown && !AltDown && !MouseLeftDown;
        private bool SelectiveDelete => MouseRightDown && ShiftDown && !CtrlDown && !AltDown && !MouseLeftDown;
        private bool SizeAndStrength => MouseRightDown && AltDown && !ShiftDown && !MouseLeftDown;
        private float[] BrushData;

        public int ID_Angle { get; private set; }
        public int ID_BrushTex { get; private set; }
        public int ID_BrushWS { get; private set; }
        public int ID_Color1 { get; private set; }
        public int ID_Color2 { get; private set; }

        public Material BrushMaterial { get; private set; }
        private Mesh BoxMesh { get; set; }
        private Dictionary<string, Texture2D> Brushes { get; set; }
        private Shader Shader => Resources.ResourceLoader.Shader;

        public Texture2D BrushTexture { get; set; }

        protected override void Awake()
        {
            base.Awake();
            enabled = false;
            BoxMesh = RenderManager.instance.OverlayEffect.GetType().GetField("m_boxMesh", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(RenderManager.instance.OverlayEffect) as Mesh;
            BrushData = new float[128 * 128];
            Brushes = Resources.ResourceLoader.LoadBrushTextures();
            ID_BrushTex = Shader.PropertyToID("_BrushTex");
            ID_BrushWS = Shader.PropertyToID("_BrushWS");
            ID_Angle = Shader.PropertyToID("_Angle");
            ID_Color1 = Shader.PropertyToID("_TerrainBrushColor1");
            ID_Color2 = Shader.PropertyToID("_TerrainBrushColor2");
            BrushMaterial = new Material(ToolsModifierControl.toolController.m_brushMaterial) { shader = Shader };
            Randomizer = new Randomizer((int)DateTime.Now.Ticks);

            FieldInfo fieldInfo = typeof(ToolController).GetField("m_tools", BindingFlags.Instance | BindingFlags.NonPublic);
            ToolBase[] tools = (ToolBase[])fieldInfo.GetValue(ToolsModifierControl.toolController);
            int initialLength = tools.Length;
            Array.Resize(ref tools, initialLength + 1);
            Dictionary<Type, ToolBase> dictionary = (Dictionary<Type, ToolBase>)typeof(ToolsModifierControl).GetField("m_Tools", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            dictionary.Add(typeof(ForestTool), this);
            tools[initialLength] = this;
            fieldInfo.SetValue(ToolsModifierControl.toolController, tools);
            ToolsModifierControl.SetTool<DefaultTool>();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            try
            {
                FieldInfo fieldInfo = typeof(ToolController).GetField("m_tools", BindingFlags.Instance | BindingFlags.NonPublic);
                List<ToolBase> tools = ((ToolBase[])fieldInfo.GetValue(ToolsModifierControl.toolController)).ToList();
                tools.Remove(this);
                fieldInfo.SetValue(ToolsModifierControl.toolController, tools.ToArray());
                Dictionary<Type, ToolBase> dictionary = (Dictionary<Type, ToolBase>)typeof(ToolsModifierControl).GetField("m_Tools", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                dictionary.Remove(typeof(ForestTool));
                if (BrushMaterial != null)
                {
                    Destroy(BrushMaterial);
                    BrushMaterial = null;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Exception caught: {exception}");
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            this.m_toolController.ClearColliding();
            ToolBase.cursorInfoLabel.textAlignment = UIHorizontalAlignment.Left;
            SetBrush(Brushes.FirstOrDefault(b => b.Key == Options.BitmapID).Value);
            ClampAngle();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            SetBrush(string.Empty);
            MouseLeftDown = false;
            MouseRightDown = false;
            MouseRayValid = false;
            ToolBase.cursorInfoLabel.textAlignment = UIHorizontalAlignment.Center;
        }

        public Dictionary<string, Texture2D> GetBrushes()
        {
            return Brushes;
        }

        public void SetBrush(string id)
        {
            if (id == null || id == string.Empty)
                return;
            if (Brushes.TryGetValue(id, out Texture2D brush))
            {
                SetBrush(brush);
                Options.BitmapID = id;
                UserMod.SaveSettings();
            }
        }

        private void SetBrush(Texture2D brush)
        {
            BrushTexture = brush;
            if (brush != null)
            {
                for (int i = 0; i < 128; i++)
                {
                    for (int j = 0; j < 128; j++)
                    {
                        BrushData[i * 128 + j] = brush.GetPixel(j, i).a;
                    }
                }
            }
        }

        protected override void OnToolGUI(Event e)
        {
            if (!this.m_toolController.IsInsideUI && e.type == EventType.MouseDown)
            {
                if (e.button == 0)
                {
                    MouseLeftDown = true;
                }
                else if (e.button == 1)
                {
                    MouseRightDown = true;
                    AxisChanged = false;
                }
            }
            else if (e.type == EventType.MouseUp)
            {
                if (e.button == 0)
                {
                    MouseLeftDown = false;
                }
                else if (e.button == 1)
                {
                    if(!AxisChanged && DensityOrRotation) Rotate45();
                    MouseRightDown = false;
                    AxisChanged = false;
                }
            }
        }

        protected override void OnToolUpdate()
        {
            if (Container is null) return;
            if (MouseRayValid)
            {
                if (UserMod.Settings.ShowInfoTooltip && (CtrlDown || AltDown))
                {
                    string density = Options.AutoDensity ? "Auto" : string.Concat(Math.Round((16 - Options.Density) * 6.25f, 1, MidpointRounding.AwayFromZero), "%");
                    string text = $"Trees: {Container.m_variations.Length}\nSize: {Options.Size}\nStrength: { Math.Round(Options.Strength * 100, 1) + "%"}\nDensity: {density}";
                    ShowInfo(true, text);
                }
                else base.ShowToolInfo(false, null, CachedPosition);
            }
            else base.ShowToolInfo(false, null, CachedPosition);
            if (MouseRightDown && (DensityOrRotation || SizeAndStrength))
            {
                float axisX = Input.GetAxis("Mouse X");
                float axisY = Input.GetAxis("Mouse Y");
                if (axisX != 0)
                {
                    AxisChanged = true;
                    if (DensityOrRotation)
                    {
                        DeltaAngle(axisX * 10.0f);
                    }
                    else if (SizeAndStrength)
                    {
                        Options.Size = Mathf.Clamp((float)Math.Round(Options.Size + axisX * (Tweaker.MaxSize / 50.0f), 1), 1.0f, Tweaker.MaxSize);
                    }
                }
                if (axisY != 0)
                {
                    AxisChanged = true;
                    if (DensityOrRotation)
                    {
                        Options.Density = Mathf.Clamp(Options.Density - axisY, 0.0f, 16.0f);
                        
                    }
                    else if (SizeAndStrength)
                    {
                        Options.Strength = Mathf.Clamp(Options.Strength + axisY * 0.1f, 0.01f, 1.0f);
                    }
                }
            }

            ForestBrush.Instance.ForestBrushPanel.BrushOptionsSection.UpdateBindings(Options);
        }

        protected void ShowInfo(bool show, string text)
        {
            if (ToolBase.cursorInfoLabel == null)
            {
                return;
            }
            if (!string.IsNullOrEmpty(text) && show)
            {
                text = kCursorInfoNormalColor + text + kCursorInfoCloseColorTag;
                ToolBase.cursorInfoLabel.isVisible = true;
                UIView uiview = ToolBase.cursorInfoLabel.GetUIView();
                Vector2 vector = (!(ToolBase.fullscreenContainer != null)) ? uiview.GetScreenResolution() : ToolBase.fullscreenContainer.size;
                Vector3 relativePosition = ForestBrush.Instance.ForestBrushPanel.absolutePosition + new Vector3(410.0f, 0.0f);
                ToolBase.cursorInfoLabel.text = text;
                if (relativePosition.x < 0f)
                {
                    relativePosition.x = 0f;
                }
                if (relativePosition.y < 0f)
                {
                    relativePosition.y = 0f;
                }
                if (relativePosition.x + ToolBase.cursorInfoLabel.width > vector.x)
                {
                    relativePosition.x = vector.x - ToolBase.cursorInfoLabel.width;
                }
                if (relativePosition.y + ToolBase.cursorInfoLabel.height > vector.y)
                {
                    relativePosition.y = vector.y - ToolBase.cursorInfoLabel.height;
                }
                ToolBase.cursorInfoLabel.relativePosition = relativePosition;
            }
            else
            {
                ToolBase.cursorInfoLabel.isVisible = false;
            }
        }

        protected override void OnToolLateUpdate()
        {
            MouseRay = Camera.main.ScreenPointToRay(Input.mousePosition);
            MouseRayLength = Camera.main.farClipPlane;
            MouseRayRight = Camera.main.transform.TransformDirection(Vector3.right);
            MouseRayValid = (!m_toolController.IsInsideUI && Cursor.visible);
            CachedPosition = MousePosition;
        }

        public override void SimulationStep()
        {
            if (Container is null) return;
            RaycastInput input = new RaycastInput(MouseRay, MouseRayLength);
            if (RayCast(input, out RaycastOutput output))
            {
                MousePosition = output.m_hitPos;
                if (MouseLeftDown != MouseRightDown) ApplyBrush();
            }
        }

        private void ApplyBrush()
        {
            if (Container is null) return;
            if (Painting && TreeCount > 0) AddTreesImpl();
            else if(Deleting && !AxisChanged) RemoveTreesImpl();
        }

        private void AddTreesImpl()
        {
            int batchSize = (int)Size * Tweaker.SizeMultiplier + Tweaker.SizeAddend;
            for (int i = 0; i < batchSize; i++)
            {
                float brushRadius = Size / 2;
                Vector2 randomPosition = UnityEngine.Random.insideUnitCircle;
                Vector3 treePosition = MousePosition + new Vector3(randomPosition.x, 0f, randomPosition.y) * Size;


                var distance = treePosition - MousePosition;
                var distanceRotated = Quaternion.Euler(0, Angle, 0) * distance;

                float brushZ = (distanceRotated.z + brushRadius) / Size * 128.0f - 0.5f;
                int z0 = Mathf.Clamp(Mathf.FloorToInt(brushZ), 0, 127);
                int z1 = Mathf.Clamp(Mathf.CeilToInt(brushZ), 0, 127);

                float brushX = (distanceRotated.x + brushRadius) / Size * 128.0f - 0.5f;
                int x0 = Mathf.Clamp(Mathf.FloorToInt(brushX), 0, 127);
                int x1 = Mathf.Clamp(Mathf.CeilToInt(brushX), 0, 127);

                float brush00 = BrushData[z0 * 128 + x0];
                float brush10 = BrushData[z0 * 128 + x1];
                float brush01 = BrushData[z1 * 128 + x0];
                float brush11 = BrushData[z1 * 128 + x1];

                float brush0 = brush00 + (brush10 - brush00) * (brushX - x0);
                float brush1 = brush01 + (brush11 - brush01) * (brushX - x0);
                float brush = brush0 + (brush1 - brush0) * (brushZ - z0);

                int change = (int)(Strength * (brush * 1.2f - 0.2f) * 10000.0f);

                if (Randomizer.Int32(10000) < change)
                {
                    TreeInfo treeInfo = Container.GetVariation(ref Randomizer);

                    treePosition.y = Singleton<TerrainManager>.instance.SampleDetailHeight(treePosition, out float f, out float f2);
                    float spacing = Options.AutoDensity ? treeInfo.m_generatedInfo.m_size.x * Tweaker.SpacingFactor : Density;
                    Randomizer tempRandomizer = Randomizer;
                    uint item = TreeManager.instance.m_trees.NextFreeItem(ref tempRandomizer);
                    Randomizer treeRandomizer = new Randomizer(item);
                    float scale = treeInfo.m_minScale + (float)treeRandomizer.Int32(10000u) * (treeInfo.m_maxScale - treeInfo.m_minScale) * 0.0001f;
                    float height = treeInfo.m_generatedInfo.m_size.y * scale;
                    float clearance = Tweaker.Clearance;
                    Vector2 treePosition2 = VectorUtils.XZ(treePosition);
                    Quad2 clearanceQuad = default(Quad2);
                    clearanceQuad.a = treePosition2 + new Vector2(-clearance, -clearance);
                    clearanceQuad.b = treePosition2 + new Vector2(-clearance, clearance);
                    clearanceQuad.c = treePosition2 + new Vector2(clearance, clearance);
                    clearanceQuad.d = treePosition2 + new Vector2(clearance, -clearance);
                    Quad2 spacingQuad = default(Quad2);
                    spacingQuad.a = treePosition2 + new Vector2(-spacing, -spacing);
                    spacingQuad.b = treePosition2 + new Vector2(-spacing, spacing);
                    spacingQuad.c = treePosition2 + new Vector2(spacing, spacing);
                    spacingQuad.d = treePosition2 + new Vector2(spacing, -spacing);
                    float minY = MousePosition.y;
                    float maxY = MousePosition.y + height;
                    ItemClass.CollisionType collisionType = ItemClass.CollisionType.Terrain;

                    if (PropManager.instance.OverlapQuad(clearanceQuad, minY, maxY, collisionType, 0, 0) && !AltDown) continue;
                    if (TreeManager.instance.OverlapQuad(spacingQuad, minY, maxY, collisionType, 0, 0)) continue;
                    if (NetManager.instance.OverlapQuad(clearanceQuad, minY, maxY, collisionType, treeInfo.m_class.m_layer, 0, 0, 0) && !AltDown) continue;
                    if (BuildingManager.instance.OverlapQuad(clearanceQuad, minY, maxY, collisionType, treeInfo.m_class.m_layer, 0, 0, 0) && !AltDown) continue;
                    if (TerrainManager.instance.HasWater(treePosition2) && !AltDown) continue;
                    int noiseScale = Randomizer.Int32(16);
                    float str2Rnd = UnityEngine.Random.Range(0.0f, Tweaker.MaxRandomRange);
                    if (Mathf.PerlinNoise(treePosition.x * noiseScale, treePosition.y * noiseScale) > 0.5 && str2Rnd < Strength * Tweaker.StrengthMultiplier)
                    {
                        if (Singleton<TreeManager>.instance.CreateTree(out uint num25, ref Randomizer, treeInfo, treePosition, false)) { }
                    }
                }
            }
        }

        private void RemoveTreesImpl()
        {
            float brushRadius = Size / 2;
            float cellSize = TreeManager.TREEGRID_CELL_SIZE;
            int resolution = TreeManager.TREEGRID_RESOLUTION;
            TreeInstance[] trees = TreeManager.instance.m_trees.m_buffer;
            uint[] treeGrid = TreeManager.instance.m_treeGrid;
            float strength = Strength;
            Vector3 position = MousePosition;

            int minX = Mathf.Max((int)((position.x - Size) / cellSize + resolution * 0.5f), 0);
            int minZ = Mathf.Max((int)((position.z - Size) / cellSize + resolution * 0.5f), 0);
            int maxX = Mathf.Min((int)((position.x + Size) / cellSize + resolution * 0.5f), resolution - 1);
            int maxZ = Mathf.Min((int)((position.z + Size) / cellSize + resolution * 0.5f), resolution - 1);

            for (int z = minZ; z <= maxZ; ++z)
            {
                for (int x = minX; x <= maxX; ++x)
                {
                    uint treeIndex = treeGrid[z * resolution + x];

                    while (treeIndex != 0)
                    {
                        uint next = trees[treeIndex].m_nextGridTree;

                        Vector3 treePosition = TreeManager.instance.m_trees.m_buffer[treeIndex].Position;

                        var distance = treePosition - position;
                        var distanceRotated = Quaternion.Euler(0, Angle, 0) * distance;

                        float brushZ = (distanceRotated.z + brushRadius) / Size * 128.0f - 0.5f;
                        int z0 = Mathf.Clamp(Mathf.FloorToInt(brushZ), 0, 127);
                        int z1 = Mathf.Clamp(Mathf.CeilToInt(brushZ), 0, 127);

                        float brushX = (distanceRotated.x + brushRadius) / Size * 128.0f - 0.5f;
                        int x0 = Mathf.Clamp(Mathf.FloorToInt(brushX), 0, 127);
                        int x1 = Mathf.Clamp(Mathf.CeilToInt(brushX), 0, 127);

                        float brush00 = BrushData[z0 * 128 + x0];
                        float brush10 = BrushData[z0 * 128 + x1];
                        float brush01 = BrushData[z1 * 128 + x0];
                        float brush11 = BrushData[z1 * 128 + x1];

                        float brush0 = brush00 + (brush10 - brush00) * (brushX - x0);
                        float brush1 = brush01 + (brush11 - brush01) * (brushX - x0);
                        float brush = brush0 + (brush1 - brush0) * (brushZ - z0);

                        int change = (int)(strength * (brush * 1.2f - 0.2f) * 10000.0f);

                        if (Randomizer.Int32(10000) < change)
                        {
                            var noiseScale = Randomizer.Int32(Tweaker.NoiseScale);
                            var strengthToRandom = UnityEngine.Random.Range(0.0f, Tweaker.MaxRandomRange);
                            TreeInfo treeInfo = TreeManager.instance.m_trees.m_buffer[treeIndex].Info;
                            if ((SelectiveDelete && ForestBrush.Instance.BrushTool.TreeInfos.Contains(treeInfo) && Mathf.PerlinNoise(position.x * noiseScale, position.y * noiseScale) > Tweaker.NoiseThreshold && strengthToRandom < UserMod.Settings.SelectedBrush.Options.Strength)
                            || !SelectiveDelete)
                            {
                                Vector2 xzTreePosition = VectorUtils.XZ(treePosition);
                                Vector2 xzMousePosition = VectorUtils.XZ(MousePosition);
                                if ((xzMousePosition - xzTreePosition).sqrMagnitude <= Size * Size) TreeManager.instance.ReleaseTree(treeIndex);
                            }
                        }
                        treeIndex = next;
                    }
                }
            }
        }

        public override void RenderOverlay(RenderManager.CameraInfo cameraInfo)
        {
            if (!MouseRayValid || Container is null) return;

            RenderBrush(cameraInfo); 
        }

        private void RenderBrush(RenderManager.CameraInfo cameraInfo)
        {
            if (BrushTexture != null)
            {
                BrushMaterial.SetTexture(ID_BrushTex, BrushTexture);
                Vector4 position = MousePosition;
                position.w = Size;
                BrushMaterial.SetVector(this.ID_BrushWS, position);
                BrushMaterial.SetFloat(ID_Angle, Angle);
                Vector3 center = new Vector3(MousePosition.x, 512f, MousePosition.z);
                Vector3 size = new Vector3(Size, 1224f, Size);
                Bounds bounds = new Bounds(center, size * 1.5f);
                ToolManager instance = Singleton<ToolManager>.instance;
                instance.m_drawCallData.m_overlayCalls = instance.m_drawCallData.m_overlayCalls + 1;
                RenderManager.instance.OverlayEffect.DrawEffect(cameraInfo, BrushMaterial, 0, bounds);
            }
        }

        private void Rotate45()
        {
            Angle = Mathf.Round(Angle / 45f - 1f) * 45f;
            ClampAngle();
        }

        private void ClampAngle()
        {
            if (Angle < 0f)
            {
                Angle += 360f;
            }
            if (Angle >= 360f)
            {
                Angle -= 360f;
            }
        }

        private  void DeltaAngle(float delta)
        {
            Angle += delta;
            ClampAngle();
        }

        public class Tweaks
        {
            public int SizeAddend;
            public int SizeMultiplier;
            public uint NoiseScale;
            public float NoiseThreshold;
            public float MaxRandomRange;
            public float Clearance;
            public float SpacingFactor;
            public float StrengthMultiplier;
            public float _maxSize;
            public float MaxSize
            {
                get
                {
                    return _maxSize;
                }
                set
                {
                    _maxSize = value;
                    ForestBrush.Instance.ForestBrushPanel.BrushOptionsSection.sizeSlider.maxValue = value;
                }
            }
        }

        public Tweaks Tweaker = new Tweaks()
        {
            SizeAddend = 10,
            SizeMultiplier = 7,
            NoiseScale = 16,
            NoiseThreshold = 0.5f,
            MaxRandomRange = 4f,
            Clearance = 4.5f,
            SpacingFactor = 0.6f,
            StrengthMultiplier = 10,
            _maxSize = 1000f
        };
    }
}
