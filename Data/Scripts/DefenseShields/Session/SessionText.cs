﻿using System.Collections.Concurrent;
using System.Collections.Generic;
using VRage.Collections;
using VRage.Game;
using VRage.Utils;
using VRageMath;
using static VRageRender.MyBillboard;

namespace DefenseShields
{
    public partial class Session
    {
        private const float MetersInPixel = 0.0002645833f;
        private const float MonoWidthScaler = 0.75f;
        private const float ShadowWidthScaler = 0.5f;
        private const float ShadowHeightScaler = 0.65f;
        private const float ShadowSizeScaler = 1.5f;

        private readonly MyConcurrentPool<AgingTextRequest> _agingTextRequestPool = new MyConcurrentPool<AgingTextRequest>(64, data => data.Clean());
        private readonly MyConcurrentPool<TextData> _textDataPool = new MyConcurrentPool<TextData>(128);

        private readonly ConcurrentDictionary<long, AgingTextRequest> _agingTextRequests = new ConcurrentDictionary<long, AgingTextRequest>();

        private readonly MyStringId _monoEnglishFontAtlas1 = MyStringId.GetOrCompute("EnglishFontMono");
        private readonly MyStringId _shadowEnglishFontAtlas1 = MyStringId.GetOrCompute("EnglishFontShadow");

        private Vector3D _viewPortSize = new Vector3D(0,0, -0.1f);


        ///
        /// 
        ///

        internal readonly Dictionary<FontType, Dictionary<char, TextureMap>> CharacterMap;
        internal readonly TextureMap[] PaintedTexture = new TextureMap[10];
        internal readonly TextureMap[] ReloadingTexture = new TextureMap[6];
        internal readonly TextureMap[] OutofAmmoTexture = new TextureMap[2];
        internal readonly TextureMap[] ChargingTexture = new TextureMap[10];
        internal readonly TextureMap[] InfoBackground = new TextureMap[3];
        internal readonly TextureMap[] HeatBarTexture = new TextureMap[12];

        internal bool NeedsUpdate = true;

        internal enum FontType
        {
            Mono,
            Shadow,
        }

        internal enum Justify
        {
            None,
            Left,
            Center,
            Right,
        }

        internal void BuildMap(MyStringId material, float initOffsetX, float initOffsetY, float offsetX, float OffsetY, float uvSizeX, float uvSizeY, float textureSizeX, float textureSizeY, ref TextureMap[] textureArr)
        {
            for (int i = 0; i < textureArr.Length; i++)
            {
                var offX = initOffsetX + (offsetX * i);
                var offY = initOffsetY + (OffsetY * i);
                textureArr[i] = GenerateMap(material, offX, offY, uvSizeX, uvSizeY, textureSizeX, textureSizeY);
            }
        }

        internal class TextureMap
        {
            internal MyStringId Material;
            internal Vector2 P0;
            internal Vector2 P1;
            internal Vector2 P2;
            internal Vector2 P3;
        }


        internal class AgingTextRequest
        {
            internal readonly CachingList<TextData> Data = new CachingList<TextData>(32);
            internal string Text;
            internal Vector4 Color;
            internal Vector3D Position;
            internal FontType Font;
            internal long ElementId;
            internal Justify Justify;
            internal float FontSize;
            internal float MessageWidth;
            internal float HeightScale;
            internal int Ttl;
            internal void Clean()
            {
                Data.Clear();
                MessageWidth = 0;
            }

        }

        internal class TextData
        {
            internal MyStringId Material;
            internal Vector3D WorldPos;
            internal Vector2 P0;
            internal Vector2 P1;
            internal Vector2 P2;
            internal Vector2 P3;
            internal float ScaledWidth;
            internal bool UvDraw;
            internal bool ReSize;
            internal BlendTypeEnum Blend = BlendTypeEnum.PostPP;
        }

        internal void UpdateHudSettings()
        {
            var fovScale = (float)(0.1 * ScaleFov);
            NeedsUpdate = false;
            _viewPortSize.X = (fovScale *  AspectRatio);
            _viewPortSize.Y = fovScale;
        }

        internal void AddText(string text, float x, float y, long elementId, int ttl, Vector4 color, Justify justify = Justify.None, FontType fontType = FontType.Shadow, float fontSize = 10f, float heightScale = 0.65f)
        {
            if (_agingTextRequests.ContainsKey(elementId) || string.IsNullOrEmpty(text))
                return;

            var request = _agingTextRequestPool.Get();

            var pos = GetScreenSpace(new Vector2(x, y));
            request.Text = text;
            request.Color = color;
            request.Position.X = pos.X;
            request.Position.Y = pos.Y;
            request.FontSize = fontSize * MetersInPixel;
            request.Font = fontType;
            request.Ttl = ttl;
            request.ElementId = elementId;
            request.Justify = justify;
            request.HeightScale = ShadowHeightScaler;
            _agingTextRequests.TryAdd(elementId, request);
        }

        internal Vector2 GetScreenSpace(Vector2 offset)
        {
            var fovScale = (float)(0.1 * ScaleFov);

            var position = new Vector2(offset.X, offset.Y);
            position.X *= fovScale * AspectRatio;
            position.Y *= fovScale;
            return position;
        }

        private void FovChanged()
        {
            NeedsUpdate = true;
        }

        internal void DrawText()
        {

            if (NeedsUpdate)
                UpdateHudSettings();

            AddAgingText();
            AgingTextDraw();
        }

        private void AddAgingText()
        {
            foreach (var aging in _agingTextRequests)
            {

                var textAdd = aging.Value;

                if (textAdd.Data.Count > 0)
                    continue;

                var scaleShadow = textAdd.Font == FontType.Shadow;
                var remap = scaleShadow ? _shadowCharWidthMap : _monoCharWidthMap;
                float messageLength = 0;
                for (int j = 0; j < textAdd.Text.Length; j++)
                {

                    var c = textAdd.Text[j];

                    float size;
                    var needResize = remap.TryGetValue(c, out size);

                    var scaledWidth = textAdd.FontSize * (needResize ? size : scaleShadow ? ShadowWidthScaler : MonoWidthScaler);
                    messageLength += scaledWidth;

                    var map = CharacterMap[textAdd.Font];

                    TextureMap cm;
                    if (!map.TryGetValue(c, out cm))
                        continue;

                    var td = _textDataPool.Get();

                    td.Material = cm.Material;
                    td.P0 = cm.P0;
                    td.P1 = cm.P1;
                    td.P2 = cm.P2;
                    td.P3 = cm.P3;
                    td.UvDraw = true;
                    td.ReSize = needResize;
                    td.ScaledWidth = scaledWidth;
                    textAdd.Data.Add(td);
                }

                textAdd.MessageWidth = messageLength;
                textAdd.Data.ApplyAdditions();
            }
        }

        private void AgingTextDraw()
        {
            var up = (Vector3) Camera.WorldMatrix.Up;
            var left = (Vector3)Camera.WorldMatrix.Left;
            foreach (var textAdd in _agingTextRequests.Values)
            {

                textAdd.Position.Z = _viewPortSize.Z;
                var requestPos = textAdd.Position;
                requestPos.Z = _viewPortSize.Z;
                var widthScaler = textAdd.Font == FontType.Shadow ? ShadowSizeScaler : 1f;

                var textPos = Vector3D.Transform(requestPos, Camera.WorldMatrix);
                switch (textAdd.Justify)
                {
                    case Justify.Center:
                        textPos += Camera.WorldMatrix.Left * (((textAdd.MessageWidth * ShadowWidthScaler) * 0.5f) * widthScaler);
                        break;
                    case Justify.Right:
                        textPos -= Camera.WorldMatrix.Left * ((textAdd.MessageWidth * ShadowWidthScaler) * widthScaler);
                        break;
                    case Justify.Left:
                        textPos -= Camera.WorldMatrix.Right * ((textAdd.MessageWidth * ShadowWidthScaler) * widthScaler);
                        break;
                    case Justify.None:
                        textPos -= Camera.WorldMatrix.Left * ((textAdd.FontSize * 0.5f) * widthScaler);
                        break;
                }

                var height = textAdd.FontSize * textAdd.HeightScale;
                var remove = textAdd.Ttl-- < 0;

                for (int i = 0; i < textAdd.Data.Count; i++)
                {

                    var textData = textAdd.Data[i];
                    textData.WorldPos.Z = _viewPortSize.Z;

                    if (textData.UvDraw)
                    {

                        var width = (textData.ScaledWidth * widthScaler) * AspectRatioInv;
                        MyQuadD quad;
                        MyUtils.GetBillboardQuadOriented(out quad, ref textPos, width, height, ref left, ref up);
                        if (textAdd.Color != Vector4.Zero)
                        {
                            MyTransparentGeometry.AddTriangleBillboard(quad.Point0, quad.Point1, quad.Point2,
                                Vector3.Zero, Vector3.Zero, Vector3.Zero, textData.P0, textData.P1, textData.P3,
                                textData.Material, 0, textPos, textAdd.Color, textData.Blend);
                            MyTransparentGeometry.AddTriangleBillboard(quad.Point0, quad.Point3, quad.Point2,
                                Vector3.Zero, Vector3.Zero, Vector3.Zero, textData.P0, textData.P2, textData.P3,
                                textData.Material, 0, textPos, textAdd.Color, textData.Blend);
                        }
                        else
                        {
                            MyTransparentGeometry.AddTriangleBillboard(quad.Point0, quad.Point1, quad.Point2,
                                Vector3.Zero, Vector3.Zero, Vector3.Zero, textData.P0, textData.P1, textData.P3,
                                textData.Material, 0, textPos, textData.Blend);
                            MyTransparentGeometry.AddTriangleBillboard(quad.Point0, quad.Point3, quad.Point2,
                                Vector3.Zero, Vector3.Zero, Vector3.Zero, textData.P0, textData.P2, textData.P3,
                                textData.Material, 0, textPos, textData.Blend);
                        }
                    }

                    textPos -= Camera.WorldMatrix.Left * textData.ScaledWidth;

                    if (remove)
                    {
                        textAdd.Data.Remove(textData);
                        _textDataPool.Return(textData);
                    }
                }

                textAdd.Data.ApplyRemovals();
                AgingTextRequest request;
                if (textAdd.Data.Count == 0 && _agingTextRequests.TryRemove(textAdd.ElementId, out request))
                {
                    _agingTextRequests.Remove(textAdd.ElementId);
                    _agingTextRequestPool.Return(request);
                }

            }
        }

        private TextureMap GenerateMap(MyStringId material, float uvOffsetX, float uvOffsetY, float uvSizeX, float uvSizeY, float textureSizeX, float textureSizeY)
        {
            var textureSize = new Vector2(textureSizeX, textureSizeY);

            return new TextureMap
            {
                Material = material,
                P0 = new Vector2(uvOffsetX, uvOffsetY) / textureSize,
                P1 = new Vector2(uvOffsetX + uvSizeX, uvOffsetY) / textureSize,
                P2 = new Vector2(uvOffsetX, uvOffsetY + uvSizeY) / textureSize,
                P3 = new Vector2(uvOffsetX + uvSizeX, uvOffsetY + uvSizeY) / textureSize,
            };
        }

        private readonly Dictionary<char, float> _shadowCharWidthMap = new Dictionary<char, float> { [' '] = 0.375f, ['.'] = 0.4f };
        private readonly Dictionary<char, float> _monoCharWidthMap = new Dictionary<char, float> { [' '] = 0.5f, ['.'] = 0.5f };

        private void LoadTextMaps(string language, out Dictionary<FontType, Dictionary<char, TextureMap>> characterMap)
        {

            switch (language)
            {
                case "CH":
                case "EN":
                default:
                    characterMap = new Dictionary<FontType, Dictionary<char, TextureMap>>
                    {
                        [FontType.Mono] = new Dictionary<char, TextureMap>
                        {
                            [' '] = GenerateMap(_monoEnglishFontAtlas1, 0, 0, 30, 42, 1024, 1024),
                            ['!'] = GenerateMap(_monoEnglishFontAtlas1, 30, 0, 30, 42, 1024, 1024),
                            ['"'] = GenerateMap(_monoEnglishFontAtlas1, 60, 0, 30, 42, 1024, 1024),
                            ['#'] = GenerateMap(_monoEnglishFontAtlas1, 90, 0, 30, 42, 1024, 1024),
                            ['$'] = GenerateMap(_monoEnglishFontAtlas1, 120, 0, 30, 42, 1024, 1024),
                            ['%'] = GenerateMap(_monoEnglishFontAtlas1, 150, 0, 30, 42, 1024, 1024),
                            ['&'] = GenerateMap(_monoEnglishFontAtlas1, 180, 0, 30, 42, 1024, 1024),
                            ['\''] = GenerateMap(_monoEnglishFontAtlas1, 210, 0, 30, 42, 1024, 1024),
                            ['('] = GenerateMap(_monoEnglishFontAtlas1, 240, 0, 30, 42, 1024, 1024),
                            [')'] = GenerateMap(_monoEnglishFontAtlas1, 270, 0, 30, 42, 1024, 1024),
                            ['*'] = GenerateMap(_monoEnglishFontAtlas1, 300, 0, 30, 42, 1024, 1024),
                            ['+'] = GenerateMap(_monoEnglishFontAtlas1, 330, 0, 30, 42, 1024, 1024),
                            [','] = GenerateMap(_monoEnglishFontAtlas1, 360, 0, 30, 42, 1024, 1024),
                            ['-'] = GenerateMap(_monoEnglishFontAtlas1, 390, 0, 30, 42, 1024, 1024),
                            ['.'] = GenerateMap(_monoEnglishFontAtlas1, 420, 0, 30, 42, 1024, 1024),
                            ['/'] = GenerateMap(_monoEnglishFontAtlas1, 450, 0, 30, 42, 1024, 1024),
                            ['0'] = GenerateMap(_monoEnglishFontAtlas1, 480, 0, 30, 42, 1024, 1024),
                            ['1'] = GenerateMap(_monoEnglishFontAtlas1, 510, 0, 30, 42, 1024, 1024),
                            ['2'] = GenerateMap(_monoEnglishFontAtlas1, 540, 0, 30, 42, 1024, 1024),
                            ['3'] = GenerateMap(_monoEnglishFontAtlas1, 570, 0, 30, 42, 1024, 1024),
                            ['4'] = GenerateMap(_monoEnglishFontAtlas1, 600, 0, 30, 42, 1024, 1024),
                            ['5'] = GenerateMap(_monoEnglishFontAtlas1, 630, 0, 30, 42, 1024, 1024),
                            ['6'] = GenerateMap(_monoEnglishFontAtlas1, 660, 0, 30, 42, 1024, 1024),
                            ['7'] = GenerateMap(_monoEnglishFontAtlas1, 690, 0, 30, 42, 1024, 1024),
                            ['8'] = GenerateMap(_monoEnglishFontAtlas1, 720, 0, 30, 42, 1024, 1024),
                            ['9'] = GenerateMap(_monoEnglishFontAtlas1, 750, 0, 30, 42, 1024, 1024),
                            [':'] = GenerateMap(_monoEnglishFontAtlas1, 780, 0, 30, 42, 1024, 1024),
                            [';'] = GenerateMap(_monoEnglishFontAtlas1, 810, 0, 30, 42, 1024, 1024),
                            ['<'] = GenerateMap(_monoEnglishFontAtlas1, 840, 0, 30, 42, 1024, 1024),
                            ['='] = GenerateMap(_monoEnglishFontAtlas1, 870, 0, 30, 42, 1024, 1024),
                            ['>'] = GenerateMap(_monoEnglishFontAtlas1, 900, 0, 30, 42, 1024, 1024),
                            ['?'] = GenerateMap(_monoEnglishFontAtlas1, 930, 0, 30, 42, 1024, 1024),
                            ['@'] = GenerateMap(_monoEnglishFontAtlas1, 960, 0, 30, 42, 1024, 1024),
                            ['A'] = GenerateMap(_monoEnglishFontAtlas1, 990, 0, 30, 42, 1024, 1024),
                            ['B'] = GenerateMap(_monoEnglishFontAtlas1, 0, 44, 30, 42, 1024, 1024),
                            ['C'] = GenerateMap(_monoEnglishFontAtlas1, 30, 44, 30, 42, 1024, 1024),
                            ['D'] = GenerateMap(_monoEnglishFontAtlas1, 60, 44, 30, 42, 1024, 1024),
                            ['E'] = GenerateMap(_monoEnglishFontAtlas1, 90, 44, 30, 42, 1024, 1024),
                            ['F'] = GenerateMap(_monoEnglishFontAtlas1, 120, 44, 30, 42, 1024, 1024),
                            ['G'] = GenerateMap(_monoEnglishFontAtlas1, 150, 44, 30, 42, 1024, 1024),
                            ['H'] = GenerateMap(_monoEnglishFontAtlas1, 180, 44, 30, 42, 1024, 1024),
                            ['I'] = GenerateMap(_monoEnglishFontAtlas1, 210, 44, 30, 42, 1024, 1024),
                            ['J'] = GenerateMap(_monoEnglishFontAtlas1, 240, 44, 30, 42, 1024, 1024),
                            ['K'] = GenerateMap(_monoEnglishFontAtlas1, 270, 44, 30, 42, 1024, 1024),
                            ['L'] = GenerateMap(_monoEnglishFontAtlas1, 300, 44, 30, 42, 1024, 1024),
                            ['M'] = GenerateMap(_monoEnglishFontAtlas1, 330, 44, 30, 42, 1024, 1024),
                            ['N'] = GenerateMap(_monoEnglishFontAtlas1, 360, 44, 30, 42, 1024, 1024),
                            ['O'] = GenerateMap(_monoEnglishFontAtlas1, 390, 44, 30, 42, 1024, 1024),
                            ['P'] = GenerateMap(_monoEnglishFontAtlas1, 420, 44, 30, 42, 1024, 1024),
                            ['Q'] = GenerateMap(_monoEnglishFontAtlas1, 450, 44, 30, 42, 1024, 1024),
                            ['R'] = GenerateMap(_monoEnglishFontAtlas1, 480, 44, 30, 42, 1024, 1024),
                            ['S'] = GenerateMap(_monoEnglishFontAtlas1, 510, 44, 30, 42, 1024, 1024),
                            ['T'] = GenerateMap(_monoEnglishFontAtlas1, 540, 44, 30, 42, 1024, 1024),
                            ['U'] = GenerateMap(_monoEnglishFontAtlas1, 570, 44, 30, 42, 1024, 1024),
                            ['V'] = GenerateMap(_monoEnglishFontAtlas1, 600, 44, 30, 42, 1024, 1024),
                            ['W'] = GenerateMap(_monoEnglishFontAtlas1, 630, 44, 30, 42, 1024, 1024),
                            ['X'] = GenerateMap(_monoEnglishFontAtlas1, 660, 44, 30, 42, 1024, 1024),
                            ['Y'] = GenerateMap(_monoEnglishFontAtlas1, 690, 44, 30, 42, 1024, 1024),
                            ['Z'] = GenerateMap(_monoEnglishFontAtlas1, 720, 44, 30, 42, 1024, 1024),
                            ['['] = GenerateMap(_monoEnglishFontAtlas1, 750, 44, 30, 42, 1024, 1024),
                            ['\\'] = GenerateMap(_monoEnglishFontAtlas1, 780, 44, 30, 42, 1024, 1024),
                            [']'] = GenerateMap(_monoEnglishFontAtlas1, 810, 44, 30, 42, 1024, 1024),
                            ['^'] = GenerateMap(_monoEnglishFontAtlas1, 840, 44, 30, 42, 1024, 1024),
                            ['_'] = GenerateMap(_monoEnglishFontAtlas1, 870, 44, 30, 42, 1024, 1024),
                            ['`'] = GenerateMap(_monoEnglishFontAtlas1, 900, 44, 30, 42, 1024, 1024),
                            ['a'] = GenerateMap(_monoEnglishFontAtlas1, 930, 44, 30, 42, 1024, 1024),
                            ['b'] = GenerateMap(_monoEnglishFontAtlas1, 960, 44, 30, 42, 1024, 1024),
                            ['c'] = GenerateMap(_monoEnglishFontAtlas1, 990, 44, 30, 42, 1024, 1024),
                            ['d'] = GenerateMap(_monoEnglishFontAtlas1, 0, 88, 30, 42, 1024, 1024),
                            ['e'] = GenerateMap(_monoEnglishFontAtlas1, 30, 88, 30, 42, 1024, 1024),
                            ['f'] = GenerateMap(_monoEnglishFontAtlas1, 60, 88, 30, 42, 1024, 1024),
                            ['g'] = GenerateMap(_monoEnglishFontAtlas1, 90, 88, 30, 42, 1024, 1024),
                            ['h'] = GenerateMap(_monoEnglishFontAtlas1, 120, 88, 30, 42, 1024, 1024),
                            ['i'] = GenerateMap(_monoEnglishFontAtlas1, 150, 88, 30, 42, 1024, 1024),
                            ['j'] = GenerateMap(_monoEnglishFontAtlas1, 180, 88, 30, 42, 1024, 1024),
                            ['k'] = GenerateMap(_monoEnglishFontAtlas1, 210, 88, 30, 42, 1024, 1024),
                            ['l'] = GenerateMap(_monoEnglishFontAtlas1, 240, 88, 30, 42, 1024, 1024),
                            ['m'] = GenerateMap(_monoEnglishFontAtlas1, 270, 88, 30, 42, 1024, 1024),
                            ['n'] = GenerateMap(_monoEnglishFontAtlas1, 300, 88, 30, 42, 1024, 1024),
                            ['o'] = GenerateMap(_monoEnglishFontAtlas1, 330, 88, 30, 42, 1024, 1024),
                            ['p'] = GenerateMap(_monoEnglishFontAtlas1, 360, 88, 30, 42, 1024, 1024),
                            ['q'] = GenerateMap(_monoEnglishFontAtlas1, 390, 88, 30, 42, 1024, 1024),
                            ['r'] = GenerateMap(_monoEnglishFontAtlas1, 420, 88, 30, 42, 1024, 1024),
                            ['s'] = GenerateMap(_monoEnglishFontAtlas1, 450, 88, 30, 42, 1024, 1024),
                            ['t'] = GenerateMap(_monoEnglishFontAtlas1, 480, 88, 30, 42, 1024, 1024),
                            ['u'] = GenerateMap(_monoEnglishFontAtlas1, 510, 88, 30, 42, 1024, 1024),
                            ['v'] = GenerateMap(_monoEnglishFontAtlas1, 540, 88, 30, 42, 1024, 1024),
                            ['w'] = GenerateMap(_monoEnglishFontAtlas1, 570, 88, 30, 42, 1024, 1024),
                            ['x'] = GenerateMap(_monoEnglishFontAtlas1, 600, 88, 30, 42, 1024, 1024),
                            ['y'] = GenerateMap(_monoEnglishFontAtlas1, 630, 88, 30, 42, 1024, 1024),
                            ['z'] = GenerateMap(_monoEnglishFontAtlas1, 660, 88, 30, 42, 1024, 1024),
                            ['{'] = GenerateMap(_monoEnglishFontAtlas1, 690, 88, 30, 42, 1024, 1024),
                            ['|'] = GenerateMap(_monoEnglishFontAtlas1, 720, 88, 30, 42, 1024, 1024),
                            ['}'] = GenerateMap(_monoEnglishFontAtlas1, 750, 88, 30, 42, 1024, 1024),
                            ['~'] = GenerateMap(_monoEnglishFontAtlas1, 780, 88, 30, 42, 1024, 1024),
                        },

                        [FontType.Shadow] = new Dictionary<char, TextureMap>()
                        {
                            [' '] = GenerateMap(_shadowEnglishFontAtlas1, 0, 0, 15, 45, 1024, 1024),
                            ['!'] = GenerateMap(_shadowEnglishFontAtlas1, 15, 0, 24, 45, 1024, 1024),
                            ['"'] = GenerateMap(_shadowEnglishFontAtlas1, 39, 0, 25, 45, 1024, 1024),
                            ['#'] = GenerateMap(_shadowEnglishFontAtlas1, 64, 0, 35, 45, 1024, 1024),
                            ['$'] = GenerateMap(_shadowEnglishFontAtlas1, 99, 0, 36, 45, 1024, 1024),
                            ['%'] = GenerateMap(_shadowEnglishFontAtlas1, 135, 0, 39, 45, 1024, 1024),
                            ['&'] = GenerateMap(_shadowEnglishFontAtlas1, 174, 0, 35, 45, 1024, 1024),
                            ['\''] = GenerateMap(_shadowEnglishFontAtlas1, 209, 0, 22, 45, 1024, 1024),
                            ['('] = GenerateMap(_shadowEnglishFontAtlas1, 231, 0, 24, 45, 1024, 1024),
                            [')'] = GenerateMap(_shadowEnglishFontAtlas1, 255, 0, 24, 45, 1024, 1024),
                            ['*'] = GenerateMap(_shadowEnglishFontAtlas1, 279, 0, 26, 45, 1024, 1024),
                            ['+'] = GenerateMap(_shadowEnglishFontAtlas1, 305, 0, 34, 45, 1024, 1024),
                            [','] = GenerateMap(_shadowEnglishFontAtlas1, 339, 0, 25, 45, 1024, 1024),
                            ['-'] = GenerateMap(_shadowEnglishFontAtlas1, 364, 0, 25, 45, 1024, 1024),
                            ['.'] = GenerateMap(_shadowEnglishFontAtlas1, 389, 0, 25, 45, 1024, 1024),
                            ['/'] = GenerateMap(_shadowEnglishFontAtlas1, 414, 0, 30, 45, 1024, 1024),
                            ['0'] = GenerateMap(_shadowEnglishFontAtlas1, 444, 0, 35, 45, 1024, 1024),
                            ['1'] = GenerateMap(_shadowEnglishFontAtlas1, 479, 0, 24, 45, 1024, 1024),
                            ['2'] = GenerateMap(_shadowEnglishFontAtlas1, 503, 0, 34, 45, 1024, 1024),
                            ['3'] = GenerateMap(_shadowEnglishFontAtlas1, 537, 0, 33, 45, 1024, 1024),
                            ['4'] = GenerateMap(_shadowEnglishFontAtlas1, 570, 0, 34, 45, 1024, 1024),
                            ['5'] = GenerateMap(_shadowEnglishFontAtlas1, 604, 0, 35, 45, 1024, 1024),
                            ['6'] = GenerateMap(_shadowEnglishFontAtlas1, 639, 0, 35, 45, 1024, 1024),
                            ['7'] = GenerateMap(_shadowEnglishFontAtlas1, 674, 0, 31, 45, 1024, 1024),
                            ['8'] = GenerateMap(_shadowEnglishFontAtlas1, 705, 0, 35, 45, 1024, 1024),
                            ['9'] = GenerateMap(_shadowEnglishFontAtlas1, 740, 0, 35, 45, 1024, 1024),
                            [':'] = GenerateMap(_shadowEnglishFontAtlas1, 775, 0, 25, 45, 1024, 1024),
                            [';'] = GenerateMap(_shadowEnglishFontAtlas1, 800, 0, 25, 45, 1024, 1024),
                            ['<'] = GenerateMap(_shadowEnglishFontAtlas1, 825, 0, 34, 45, 1024, 1024),
                            ['='] = GenerateMap(_shadowEnglishFontAtlas1, 859, 0, 34, 45, 1024, 1024),
                            ['>'] = GenerateMap(_shadowEnglishFontAtlas1, 893, 0, 34, 45, 1024, 1024),
                            ['?'] = GenerateMap(_shadowEnglishFontAtlas1, 927, 0, 31, 45, 1024, 1024),
                            ['@'] = GenerateMap(_shadowEnglishFontAtlas1, 958, 0, 40, 45, 1024, 1024),
                            ['A'] = GenerateMap(_shadowEnglishFontAtlas1, 0, 45, 37, 45, 1024, 1024),
                            ['B'] = GenerateMap(_shadowEnglishFontAtlas1, 37, 45, 37, 45, 1024, 1024),
                            ['C'] = GenerateMap(_shadowEnglishFontAtlas1, 74, 45, 35, 45, 1024, 1024),
                            ['D'] = GenerateMap(_shadowEnglishFontAtlas1, 109, 45, 37, 45, 1024, 1024),
                            ['E'] = GenerateMap(_shadowEnglishFontAtlas1, 146, 45, 34, 45, 1024, 1024),
                            ['F'] = GenerateMap(_shadowEnglishFontAtlas1, 180, 45, 32, 45, 1024, 1024),
                            ['G'] = GenerateMap(_shadowEnglishFontAtlas1, 212, 45, 36, 45, 1024, 1024),
                            ['H'] = GenerateMap(_shadowEnglishFontAtlas1, 248, 45, 35, 45, 1024, 1024),
                            ['I'] = GenerateMap(_shadowEnglishFontAtlas1, 283, 45, 24, 45, 1024, 1024),
                            ['J'] = GenerateMap(_shadowEnglishFontAtlas1, 307, 45, 31, 45, 1024, 1024),
                            ['K'] = GenerateMap(_shadowEnglishFontAtlas1, 338, 45, 33, 45, 1024, 1024),
                            ['L'] = GenerateMap(_shadowEnglishFontAtlas1, 371, 45, 30, 45, 1024, 1024),
                            ['M'] = GenerateMap(_shadowEnglishFontAtlas1, 401, 45, 42, 45, 1024, 1024),
                            ['N'] = GenerateMap(_shadowEnglishFontAtlas1, 443, 45, 37, 45, 1024, 1024),
                            ['O'] = GenerateMap(_shadowEnglishFontAtlas1, 480, 45, 37, 45, 1024, 1024),
                            ['P'] = GenerateMap(_shadowEnglishFontAtlas1, 517, 45, 35, 45, 1024, 1024),
                            ['Q'] = GenerateMap(_shadowEnglishFontAtlas1, 552, 45, 37, 45, 1024, 1024),
                            ['R'] = GenerateMap(_shadowEnglishFontAtlas1, 589, 45, 37, 45, 1024, 1024),
                            ['S'] = GenerateMap(_shadowEnglishFontAtlas1, 626, 45, 37, 45, 1024, 1024),
                            ['T'] = GenerateMap(_shadowEnglishFontAtlas1, 663, 45, 32, 45, 1024, 1024),
                            ['U'] = GenerateMap(_shadowEnglishFontAtlas1, 695, 45, 36, 45, 1024, 1024),
                            ['V'] = GenerateMap(_shadowEnglishFontAtlas1, 731, 45, 35, 45, 1024, 1024),
                            ['W'] = GenerateMap(_shadowEnglishFontAtlas1, 766, 45, 47, 45, 1024, 1024),
                            ['X'] = GenerateMap(_shadowEnglishFontAtlas1, 813, 45, 35, 45, 1024, 1024),
                            ['Y'] = GenerateMap(_shadowEnglishFontAtlas1, 848, 45, 36, 45, 1024, 1024),
                            ['Z'] = GenerateMap(_shadowEnglishFontAtlas1, 884, 45, 35, 45, 1024, 1024),
                            ['['] = GenerateMap(_shadowEnglishFontAtlas1, 919, 45, 25, 45, 1024, 1024),
                            ['\\'] = GenerateMap(_shadowEnglishFontAtlas1, 944, 45, 28, 45, 1024, 1024),
                            [']'] = GenerateMap(_shadowEnglishFontAtlas1, 972, 45, 25, 45, 1024, 1024),
                            ['^'] = GenerateMap(_shadowEnglishFontAtlas1, 0, 90, 34, 45, 1024, 1024),
                            ['_'] = GenerateMap(_shadowEnglishFontAtlas1, 34, 90, 31, 45, 1024, 1024),
                            ['`'] = GenerateMap(_shadowEnglishFontAtlas1, 65, 90, 23, 45, 1024, 1024),
                            ['a'] = GenerateMap(_shadowEnglishFontAtlas1, 88, 90, 33, 45, 1024, 1024),
                            ['b'] = GenerateMap(_shadowEnglishFontAtlas1, 121, 90, 33, 45, 1024, 1024),
                            ['c'] = GenerateMap(_shadowEnglishFontAtlas1, 154, 90, 32, 45, 1024, 1024),
                            ['d'] = GenerateMap(_shadowEnglishFontAtlas1, 186, 90, 33, 45, 1024, 1024),
                            ['e'] = GenerateMap(_shadowEnglishFontAtlas1, 219, 90, 33, 45, 1024, 1024),
                            ['f'] = GenerateMap(_shadowEnglishFontAtlas1, 252, 90, 24, 45, 1024, 1024),
                            ['g'] = GenerateMap(_shadowEnglishFontAtlas1, 276, 90, 33, 45, 1024, 1024),
                            ['h'] = GenerateMap(_shadowEnglishFontAtlas1, 309, 90, 33, 45, 1024, 1024),
                            ['i'] = GenerateMap(_shadowEnglishFontAtlas1, 342, 90, 23, 45, 1024, 1024),
                            ['j'] = GenerateMap(_shadowEnglishFontAtlas1, 365, 90, 23, 45, 1024, 1024),
                            ['k'] = GenerateMap(_shadowEnglishFontAtlas1, 388, 90, 32, 45, 1024, 1024),
                            ['l'] = GenerateMap(_shadowEnglishFontAtlas1, 420, 90, 23, 45, 1024, 1024),
                            ['m'] = GenerateMap(_shadowEnglishFontAtlas1, 443, 90, 42, 45, 1024, 1024),
                            ['n'] = GenerateMap(_shadowEnglishFontAtlas1, 485, 90, 33, 45, 1024, 1024),
                            ['o'] = GenerateMap(_shadowEnglishFontAtlas1, 518, 90, 33, 45, 1024, 1024),
                            ['p'] = GenerateMap(_shadowEnglishFontAtlas1, 551, 90, 33, 45, 1024, 1024),
                            ['q'] = GenerateMap(_shadowEnglishFontAtlas1, 584, 90, 33, 45, 1024, 1024),
                            ['r'] = GenerateMap(_shadowEnglishFontAtlas1, 617, 90, 25, 45, 1024, 1024),
                            ['s'] = GenerateMap(_shadowEnglishFontAtlas1, 642, 90, 33, 45, 1024, 1024),
                            ['t'] = GenerateMap(_shadowEnglishFontAtlas1, 675, 90, 25, 45, 1024, 1024),
                            ['u'] = GenerateMap(_shadowEnglishFontAtlas1, 700, 90, 33, 45, 1024, 1024),
                            ['v'] = GenerateMap(_shadowEnglishFontAtlas1, 733, 90, 30, 45, 1024, 1024),
                            ['w'] = GenerateMap(_shadowEnglishFontAtlas1, 763, 90, 42, 45, 1024, 1024),
                            ['x'] = GenerateMap(_shadowEnglishFontAtlas1, 805, 90, 31, 45, 1024, 1024),
                            ['y'] = GenerateMap(_shadowEnglishFontAtlas1, 836, 90, 33, 45, 1024, 1024),
                            ['z'] = GenerateMap(_shadowEnglishFontAtlas1, 869, 90, 31, 45, 1024, 1024),
                            ['{'] = GenerateMap(_shadowEnglishFontAtlas1, 900, 90, 25, 45, 1024, 1024),
                            ['|'] = GenerateMap(_shadowEnglishFontAtlas1, 925, 90, 22, 45, 1024, 1024),
                            ['}'] = GenerateMap(_shadowEnglishFontAtlas1, 947, 90, 25, 45, 1024, 1024),
                            ['~'] = GenerateMap(_shadowEnglishFontAtlas1, 972, 90, 34, 45, 1024, 1024),
                            [' '] = GenerateMap(_shadowEnglishFontAtlas1, 0, 135, 23, 45, 1024, 1024),
                            ['¡'] = GenerateMap(_shadowEnglishFontAtlas1, 23, 135, 24, 45, 1024, 1024),
                            ['¢'] = GenerateMap(_shadowEnglishFontAtlas1, 47, 135, 32, 45, 1024, 1024),
                            ['£'] = GenerateMap(_shadowEnglishFontAtlas1, 79, 135, 33, 45, 1024, 1024),
                            ['¤'] = GenerateMap(_shadowEnglishFontAtlas1, 112, 135, 35, 45, 1024, 1024),
                            ['¥'] = GenerateMap(_shadowEnglishFontAtlas1, 147, 135, 35, 45, 1024, 1024),
                            ['¦'] = GenerateMap(_shadowEnglishFontAtlas1, 182, 135, 22, 45, 1024, 1024),
                            ['§'] = GenerateMap(_shadowEnglishFontAtlas1, 204, 135, 36, 45, 1024, 1024),
                            ['¨'] = GenerateMap(_shadowEnglishFontAtlas1, 240, 135, 23, 45, 1024, 1024),
                            ['©'] = GenerateMap(_shadowEnglishFontAtlas1, 263, 135, 40, 45, 1024, 1024),
                            ['ª'] = GenerateMap(_shadowEnglishFontAtlas1, 303, 135, 26, 45, 1024, 1024),
                            ['«'] = GenerateMap(_shadowEnglishFontAtlas1, 329, 135, 30, 45, 1024, 1024),
                            ['¬'] = GenerateMap(_shadowEnglishFontAtlas1, 359, 135, 34, 45, 1024, 1024),
                            ['­'] = GenerateMap(_shadowEnglishFontAtlas1, 393, 135, 14, 8, 1024, 1024),
                            ['®'] = GenerateMap(_shadowEnglishFontAtlas1, 407, 135, 40, 45, 1024, 1024),
                            ['¯'] = GenerateMap(_shadowEnglishFontAtlas1, 447, 135, 23, 45, 1024, 1024),
                            ['°'] = GenerateMap(_shadowEnglishFontAtlas1, 470, 135, 27, 45, 1024, 1024),
                            ['±'] = GenerateMap(_shadowEnglishFontAtlas1, 497, 135, 34, 45, 1024, 1024),
                            ['²'] = GenerateMap(_shadowEnglishFontAtlas1, 531, 135, 27, 45, 1024, 1024),
                            ['³'] = GenerateMap(_shadowEnglishFontAtlas1, 558, 135, 27, 45, 1024, 1024),
                            ['´'] = GenerateMap(_shadowEnglishFontAtlas1, 585, 135, 23, 45, 1024, 1024),
                            ['µ'] = GenerateMap(_shadowEnglishFontAtlas1, 608, 135, 33, 45, 1024, 1024),
                            ['¶'] = GenerateMap(_shadowEnglishFontAtlas1, 641, 135, 34, 45, 1024, 1024),
                            ['·'] = GenerateMap(_shadowEnglishFontAtlas1, 675, 135, 25, 45, 1024, 1024),
                            ['¸'] = GenerateMap(_shadowEnglishFontAtlas1, 700, 135, 23, 45, 1024, 1024),
                            ['¹'] = GenerateMap(_shadowEnglishFontAtlas1, 723, 135, 27, 45, 1024, 1024),
                            ['º'] = GenerateMap(_shadowEnglishFontAtlas1, 750, 135, 26, 45, 1024, 1024),
                            ['»'] = GenerateMap(_shadowEnglishFontAtlas1, 776, 135, 30, 45, 1024, 1024),
                            ['¼'] = GenerateMap(_shadowEnglishFontAtlas1, 806, 135, 43, 45, 1024, 1024),
                            ['½'] = GenerateMap(_shadowEnglishFontAtlas1, 849, 135, 45, 45, 1024, 1024),
                            ['¾'] = GenerateMap(_shadowEnglishFontAtlas1, 894, 135, 43, 45, 1024, 1024),
                            ['¿'] = GenerateMap(_shadowEnglishFontAtlas1, 937, 135, 31, 45, 1024, 1024),
                            ['À'] = GenerateMap(_shadowEnglishFontAtlas1, 968, 135, 37, 45, 1024, 1024),
                            ['Á'] = GenerateMap(_shadowEnglishFontAtlas1, 0, 180, 37, 45, 1024, 1024),
                            ['Â'] = GenerateMap(_shadowEnglishFontAtlas1, 37, 180, 37, 45, 1024, 1024),
                            ['Ã'] = GenerateMap(_shadowEnglishFontAtlas1, 74, 180, 37, 45, 1024, 1024),
                            ['Ä'] = GenerateMap(_shadowEnglishFontAtlas1, 111, 180, 37, 45, 1024, 1024),
                            ['Å'] = GenerateMap(_shadowEnglishFontAtlas1, 148, 180, 37, 45, 1024, 1024),
                            ['Æ'] = GenerateMap(_shadowEnglishFontAtlas1, 185, 180, 47, 45, 1024, 1024),
                            ['Ç'] = GenerateMap(_shadowEnglishFontAtlas1, 232, 180, 35, 45, 1024, 1024),
                            ['È'] = GenerateMap(_shadowEnglishFontAtlas1, 267, 180, 34, 45, 1024, 1024),
                            ['É'] = GenerateMap(_shadowEnglishFontAtlas1, 301, 180, 34, 45, 1024, 1024),
                            ['Ê'] = GenerateMap(_shadowEnglishFontAtlas1, 335, 180, 34, 45, 1024, 1024),
                            ['Ë'] = GenerateMap(_shadowEnglishFontAtlas1, 369, 180, 34, 45, 1024, 1024),
                            ['Ì'] = GenerateMap(_shadowEnglishFontAtlas1, 403, 180, 24, 45, 1024, 1024),
                            ['Í'] = GenerateMap(_shadowEnglishFontAtlas1, 427, 180, 24, 45, 1024, 1024),
                            ['Î'] = GenerateMap(_shadowEnglishFontAtlas1, 451, 180, 24, 45, 1024, 1024),
                            ['Ï'] = GenerateMap(_shadowEnglishFontAtlas1, 475, 180, 24, 45, 1024, 1024),
                            ['Ð'] = GenerateMap(_shadowEnglishFontAtlas1, 499, 180, 37, 45, 1024, 1024),
                            ['Ñ'] = GenerateMap(_shadowEnglishFontAtlas1, 536, 180, 37, 45, 1024, 1024),
                            ['Ò'] = GenerateMap(_shadowEnglishFontAtlas1, 573, 180, 37, 45, 1024, 1024),
                            ['Ó'] = GenerateMap(_shadowEnglishFontAtlas1, 610, 180, 37, 45, 1024, 1024),
                            ['Ô'] = GenerateMap(_shadowEnglishFontAtlas1, 647, 180, 37, 45, 1024, 1024),
                            ['Õ'] = GenerateMap(_shadowEnglishFontAtlas1, 684, 180, 37, 45, 1024, 1024),
                            ['Ö'] = GenerateMap(_shadowEnglishFontAtlas1, 721, 180, 37, 45, 1024, 1024),
                            ['×'] = GenerateMap(_shadowEnglishFontAtlas1, 758, 180, 34, 45, 1024, 1024),
                            ['Ø'] = GenerateMap(_shadowEnglishFontAtlas1, 792, 180, 37, 45, 1024, 1024),
                            ['Ù'] = GenerateMap(_shadowEnglishFontAtlas1, 829, 180, 36, 45, 1024, 1024),
                            ['Ú'] = GenerateMap(_shadowEnglishFontAtlas1, 865, 180, 36, 45, 1024, 1024),
                            ['Û'] = GenerateMap(_shadowEnglishFontAtlas1, 901, 180, 36, 45, 1024, 1024),
                            ['Ü'] = GenerateMap(_shadowEnglishFontAtlas1, 937, 180, 36, 45, 1024, 1024),
                            ['Ý'] = GenerateMap(_shadowEnglishFontAtlas1, 973, 180, 33, 45, 1024, 1024),
                            ['Þ'] = GenerateMap(_shadowEnglishFontAtlas1, 0, 225, 35, 45, 1024, 1024),
                            ['ß'] = GenerateMap(_shadowEnglishFontAtlas1, 35, 225, 34, 45, 1024, 1024),
                            ['à'] = GenerateMap(_shadowEnglishFontAtlas1, 69, 225, 33, 45, 1024, 1024),
                            ['á'] = GenerateMap(_shadowEnglishFontAtlas1, 102, 225, 33, 45, 1024, 1024),
                            ['â'] = GenerateMap(_shadowEnglishFontAtlas1, 135, 225, 33, 45, 1024, 1024),
                            ['ã'] = GenerateMap(_shadowEnglishFontAtlas1, 168, 225, 33, 45, 1024, 1024),
                            ['ä'] = GenerateMap(_shadowEnglishFontAtlas1, 201, 225, 33, 45, 1024, 1024),
                            ['å'] = GenerateMap(_shadowEnglishFontAtlas1, 234, 225, 33, 45, 1024, 1024),
                            ['æ'] = GenerateMap(_shadowEnglishFontAtlas1, 267, 225, 44, 45, 1024, 1024),
                            ['ç'] = GenerateMap(_shadowEnglishFontAtlas1, 311, 225, 32, 45, 1024, 1024),
                            ['è'] = GenerateMap(_shadowEnglishFontAtlas1, 343, 225, 33, 45, 1024, 1024),
                            ['é'] = GenerateMap(_shadowEnglishFontAtlas1, 376, 225, 33, 45, 1024, 1024),
                            ['ê'] = GenerateMap(_shadowEnglishFontAtlas1, 409, 225, 33, 45, 1024, 1024),
                            ['ë'] = GenerateMap(_shadowEnglishFontAtlas1, 442, 225, 33, 45, 1024, 1024),
                            ['ì'] = GenerateMap(_shadowEnglishFontAtlas1, 475, 225, 23, 45, 1024, 1024),
                            ['í'] = GenerateMap(_shadowEnglishFontAtlas1, 498, 225, 23, 45, 1024, 1024),
                            ['î'] = GenerateMap(_shadowEnglishFontAtlas1, 521, 225, 23, 45, 1024, 1024),
                            ['ï'] = GenerateMap(_shadowEnglishFontAtlas1, 544, 225, 23, 45, 1024, 1024),
                            ['ð'] = GenerateMap(_shadowEnglishFontAtlas1, 567, 225, 33, 45, 1024, 1024),
                            ['ñ'] = GenerateMap(_shadowEnglishFontAtlas1, 600, 225, 33, 45, 1024, 1024),
                            ['ò'] = GenerateMap(_shadowEnglishFontAtlas1, 633, 225, 33, 45, 1024, 1024),
                            ['ó'] = GenerateMap(_shadowEnglishFontAtlas1, 666, 225, 33, 45, 1024, 1024),
                            ['ô'] = GenerateMap(_shadowEnglishFontAtlas1, 699, 225, 33, 45, 1024, 1024),
                            ['õ'] = GenerateMap(_shadowEnglishFontAtlas1, 732, 225, 33, 45, 1024, 1024),
                            ['ö'] = GenerateMap(_shadowEnglishFontAtlas1, 765, 225, 33, 45, 1024, 1024),
                            ['÷'] = GenerateMap(_shadowEnglishFontAtlas1, 798, 225, 34, 45, 1024, 1024),
                            ['ø'] = GenerateMap(_shadowEnglishFontAtlas1, 832, 225, 33, 45, 1024, 1024),
                            ['ù'] = GenerateMap(_shadowEnglishFontAtlas1, 865, 225, 33, 45, 1024, 1024),
                            ['ú'] = GenerateMap(_shadowEnglishFontAtlas1, 898, 225, 33, 45, 1024, 1024),
                            ['û'] = GenerateMap(_shadowEnglishFontAtlas1, 931, 225, 33, 45, 1024, 1024),
                            ['ü'] = GenerateMap(_shadowEnglishFontAtlas1, 964, 225, 33, 45, 1024, 1024),
                            ['ý'] = GenerateMap(_shadowEnglishFontAtlas1, 0, 270, 33, 45, 1024, 1024),
                            ['þ'] = GenerateMap(_shadowEnglishFontAtlas1, 33, 270, 33, 45, 1024, 1024),
                            ['ÿ'] = GenerateMap(_shadowEnglishFontAtlas1, 66, 270, 33, 45, 1024, 1024),
                            ['Ā'] = GenerateMap(_shadowEnglishFontAtlas1, 99, 270, 35, 45, 1024, 1024),
                            ['ā'] = GenerateMap(_shadowEnglishFontAtlas1, 134, 270, 33, 45, 1024, 1024),
                            ['Ă'] = GenerateMap(_shadowEnglishFontAtlas1, 167, 270, 37, 45, 1024, 1024),
                            ['ă'] = GenerateMap(_shadowEnglishFontAtlas1, 204, 270, 33, 45, 1024, 1024),
                            ['Ą'] = GenerateMap(_shadowEnglishFontAtlas1, 237, 270, 37, 45, 1024, 1024),
                            ['ą'] = GenerateMap(_shadowEnglishFontAtlas1, 274, 270, 33, 45, 1024, 1024),
                            ['Ć'] = GenerateMap(_shadowEnglishFontAtlas1, 307, 270, 35, 45, 1024, 1024),
                            ['ć'] = GenerateMap(_shadowEnglishFontAtlas1, 342, 270, 32, 45, 1024, 1024),
                            ['Ĉ'] = GenerateMap(_shadowEnglishFontAtlas1, 374, 270, 35, 45, 1024, 1024),
                            ['ĉ'] = GenerateMap(_shadowEnglishFontAtlas1, 409, 270, 32, 45, 1024, 1024),
                            ['Ċ'] = GenerateMap(_shadowEnglishFontAtlas1, 441, 270, 35, 45, 1024, 1024),
                            ['ċ'] = GenerateMap(_shadowEnglishFontAtlas1, 476, 270, 32, 45, 1024, 1024),
                            ['Č'] = GenerateMap(_shadowEnglishFontAtlas1, 508, 270, 35, 45, 1024, 1024),
                            ['č'] = GenerateMap(_shadowEnglishFontAtlas1, 543, 270, 32, 45, 1024, 1024),
                            ['Ď'] = GenerateMap(_shadowEnglishFontAtlas1, 575, 270, 37, 45, 1024, 1024),
                            ['ď'] = GenerateMap(_shadowEnglishFontAtlas1, 612, 270, 33, 45, 1024, 1024),
                            ['Đ'] = GenerateMap(_shadowEnglishFontAtlas1, 645, 270, 37, 45, 1024, 1024),
                            ['đ'] = GenerateMap(_shadowEnglishFontAtlas1, 682, 270, 33, 45, 1024, 1024),
                            ['Ē'] = GenerateMap(_shadowEnglishFontAtlas1, 715, 270, 34, 45, 1024, 1024),
                            ['ē'] = GenerateMap(_shadowEnglishFontAtlas1, 749, 270, 33, 45, 1024, 1024),
                            ['Ĕ'] = GenerateMap(_shadowEnglishFontAtlas1, 782, 270, 34, 45, 1024, 1024),
                            ['ĕ'] = GenerateMap(_shadowEnglishFontAtlas1, 816, 270, 33, 45, 1024, 1024),
                            ['Ė'] = GenerateMap(_shadowEnglishFontAtlas1, 849, 270, 34, 45, 1024, 1024),
                            ['ė'] = GenerateMap(_shadowEnglishFontAtlas1, 883, 270, 33, 45, 1024, 1024),
                            ['Ę'] = GenerateMap(_shadowEnglishFontAtlas1, 916, 270, 34, 45, 1024, 1024),
                            ['ę'] = GenerateMap(_shadowEnglishFontAtlas1, 950, 270, 33, 45, 1024, 1024),
                            ['Ě'] = GenerateMap(_shadowEnglishFontAtlas1, 983, 270, 34, 45, 1024, 1024),
                            ['ě'] = GenerateMap(_shadowEnglishFontAtlas1, 0, 315, 33, 45, 1024, 1024),
                            ['Ĝ'] = GenerateMap(_shadowEnglishFontAtlas1, 33, 315, 36, 45, 1024, 1024),
                            ['ĝ'] = GenerateMap(_shadowEnglishFontAtlas1, 69, 315, 33, 45, 1024, 1024),
                            ['Ğ'] = GenerateMap(_shadowEnglishFontAtlas1, 102, 315, 36, 45, 1024, 1024),
                            ['ğ'] = GenerateMap(_shadowEnglishFontAtlas1, 138, 315, 33, 45, 1024, 1024),
                            ['Ġ'] = GenerateMap(_shadowEnglishFontAtlas1, 171, 315, 36, 45, 1024, 1024),
                            ['ġ'] = GenerateMap(_shadowEnglishFontAtlas1, 207, 315, 33, 45, 1024, 1024),
                            ['Ģ'] = GenerateMap(_shadowEnglishFontAtlas1, 240, 315, 36, 45, 1024, 1024),
                            ['ģ'] = GenerateMap(_shadowEnglishFontAtlas1, 276, 315, 33, 45, 1024, 1024),
                            ['Ĥ'] = GenerateMap(_shadowEnglishFontAtlas1, 309, 315, 35, 45, 1024, 1024),
                            ['ĥ'] = GenerateMap(_shadowEnglishFontAtlas1, 344, 315, 33, 45, 1024, 1024),
                            ['Ħ'] = GenerateMap(_shadowEnglishFontAtlas1, 377, 315, 35, 45, 1024, 1024),
                            ['ħ'] = GenerateMap(_shadowEnglishFontAtlas1, 412, 315, 33, 45, 1024, 1024),
                            ['Ĩ'] = GenerateMap(_shadowEnglishFontAtlas1, 445, 315, 24, 45, 1024, 1024),
                            ['ĩ'] = GenerateMap(_shadowEnglishFontAtlas1, 469, 315, 23, 45, 1024, 1024),
                            ['Ī'] = GenerateMap(_shadowEnglishFontAtlas1, 492, 315, 24, 45, 1024, 1024),
                            ['ī'] = GenerateMap(_shadowEnglishFontAtlas1, 516, 315, 23, 45, 1024, 1024),
                            ['Į'] = GenerateMap(_shadowEnglishFontAtlas1, 539, 315, 24, 45, 1024, 1024),
                            ['į'] = GenerateMap(_shadowEnglishFontAtlas1, 563, 315, 23, 45, 1024, 1024),
                            ['İ'] = GenerateMap(_shadowEnglishFontAtlas1, 586, 315, 24, 45, 1024, 1024),
                            ['ı'] = GenerateMap(_shadowEnglishFontAtlas1, 610, 315, 23, 45, 1024, 1024),
                            ['Ĳ'] = GenerateMap(_shadowEnglishFontAtlas1, 633, 315, 40, 45, 1024, 1024),
                            ['ĳ'] = GenerateMap(_shadowEnglishFontAtlas1, 673, 315, 29, 45, 1024, 1024),
                            ['Ĵ'] = GenerateMap(_shadowEnglishFontAtlas1, 702, 315, 31, 45, 1024, 1024),
                            ['ĵ'] = GenerateMap(_shadowEnglishFontAtlas1, 733, 315, 23, 45, 1024, 1024),
                            ['Ķ'] = GenerateMap(_shadowEnglishFontAtlas1, 756, 315, 33, 45, 1024, 1024),
                            ['ķ'] = GenerateMap(_shadowEnglishFontAtlas1, 789, 315, 32, 45, 1024, 1024),
                            ['Ĺ'] = GenerateMap(_shadowEnglishFontAtlas1, 821, 315, 30, 45, 1024, 1024),
                            ['ĺ'] = GenerateMap(_shadowEnglishFontAtlas1, 851, 315, 23, 45, 1024, 1024),
                            ['Ļ'] = GenerateMap(_shadowEnglishFontAtlas1, 874, 315, 30, 45, 1024, 1024),
                            ['ļ'] = GenerateMap(_shadowEnglishFontAtlas1, 904, 315, 23, 45, 1024, 1024),
                            ['Ľ'] = GenerateMap(_shadowEnglishFontAtlas1, 927, 315, 30, 45, 1024, 1024),
                            ['ľ'] = GenerateMap(_shadowEnglishFontAtlas1, 957, 315, 23, 45, 1024, 1024),
                            ['Ŀ'] = GenerateMap(_shadowEnglishFontAtlas1, 980, 315, 30, 45, 1024, 1024),
                            ['ŀ'] = GenerateMap(_shadowEnglishFontAtlas1, 0, 360, 26, 45, 1024, 1024),
                            ['Ł'] = GenerateMap(_shadowEnglishFontAtlas1, 26, 360, 30, 45, 1024, 1024),
                            ['ł'] = GenerateMap(_shadowEnglishFontAtlas1, 56, 360, 23, 45, 1024, 1024),
                            ['Ń'] = GenerateMap(_shadowEnglishFontAtlas1, 79, 360, 37, 45, 1024, 1024),
                            ['ń'] = GenerateMap(_shadowEnglishFontAtlas1, 116, 360, 33, 45, 1024, 1024),
                            ['Ņ'] = GenerateMap(_shadowEnglishFontAtlas1, 149, 360, 37, 45, 1024, 1024),
                            ['ņ'] = GenerateMap(_shadowEnglishFontAtlas1, 186, 360, 33, 45, 1024, 1024),
                            ['Ň'] = GenerateMap(_shadowEnglishFontAtlas1, 219, 360, 37, 45, 1024, 1024),
                            ['ň'] = GenerateMap(_shadowEnglishFontAtlas1, 256, 360, 33, 45, 1024, 1024),
                            ['ŉ'] = GenerateMap(_shadowEnglishFontAtlas1, 289, 360, 33, 45, 1024, 1024),
                            ['Ō'] = GenerateMap(_shadowEnglishFontAtlas1, 322, 360, 37, 45, 1024, 1024),
                            ['ō'] = GenerateMap(_shadowEnglishFontAtlas1, 359, 360, 33, 45, 1024, 1024),
                            ['Ŏ'] = GenerateMap(_shadowEnglishFontAtlas1, 392, 360, 37, 45, 1024, 1024),
                            ['ŏ'] = GenerateMap(_shadowEnglishFontAtlas1, 429, 360, 33, 45, 1024, 1024),
                            ['Ő'] = GenerateMap(_shadowEnglishFontAtlas1, 462, 360, 37, 45, 1024, 1024),
                            ['ő'] = GenerateMap(_shadowEnglishFontAtlas1, 499, 360, 33, 45, 1024, 1024),
                            ['Œ'] = GenerateMap(_shadowEnglishFontAtlas1, 532, 360, 47, 45, 1024, 1024),
                            ['œ'] = GenerateMap(_shadowEnglishFontAtlas1, 579, 360, 44, 45, 1024, 1024),
                            ['Ŕ'] = GenerateMap(_shadowEnglishFontAtlas1, 623, 360, 37, 45, 1024, 1024),
                            ['ŕ'] = GenerateMap(_shadowEnglishFontAtlas1, 660, 360, 25, 45, 1024, 1024),
                            ['Ŗ'] = GenerateMap(_shadowEnglishFontAtlas1, 685, 360, 37, 45, 1024, 1024),
                            ['ŗ'] = GenerateMap(_shadowEnglishFontAtlas1, 722, 360, 25, 45, 1024, 1024),
                            ['Ř'] = GenerateMap(_shadowEnglishFontAtlas1, 747, 360, 37, 45, 1024, 1024),
                            ['ř'] = GenerateMap(_shadowEnglishFontAtlas1, 784, 360, 25, 45, 1024, 1024),
                            ['Ś'] = GenerateMap(_shadowEnglishFontAtlas1, 809, 360, 37, 45, 1024, 1024),
                            ['ś'] = GenerateMap(_shadowEnglishFontAtlas1, 846, 360, 33, 45, 1024, 1024),
                            ['Ŝ'] = GenerateMap(_shadowEnglishFontAtlas1, 879, 360, 37, 45, 1024, 1024),
                            ['ŝ'] = GenerateMap(_shadowEnglishFontAtlas1, 916, 360, 33, 45, 1024, 1024),
                            ['Ş'] = GenerateMap(_shadowEnglishFontAtlas1, 949, 360, 37, 45, 1024, 1024),
                            ['ş'] = GenerateMap(_shadowEnglishFontAtlas1, 986, 360, 33, 45, 1024, 1024),
                            ['Š'] = GenerateMap(_shadowEnglishFontAtlas1, 0, 405, 37, 45, 1024, 1024),
                            ['š'] = GenerateMap(_shadowEnglishFontAtlas1, 37, 405, 33, 45, 1024, 1024),
                            ['Ţ'] = GenerateMap(_shadowEnglishFontAtlas1, 70, 405, 32, 45, 1024, 1024),
                            ['ţ'] = GenerateMap(_shadowEnglishFontAtlas1, 102, 405, 25, 45, 1024, 1024),
                            ['Ť'] = GenerateMap(_shadowEnglishFontAtlas1, 127, 405, 32, 45, 1024, 1024),
                            ['ť'] = GenerateMap(_shadowEnglishFontAtlas1, 159, 405, 25, 45, 1024, 1024),
                            ['Ŧ'] = GenerateMap(_shadowEnglishFontAtlas1, 184, 405, 32, 45, 1024, 1024),
                            ['ŧ'] = GenerateMap(_shadowEnglishFontAtlas1, 216, 405, 25, 45, 1024, 1024),
                            ['Ũ'] = GenerateMap(_shadowEnglishFontAtlas1, 241, 405, 36, 45, 1024, 1024),
                            ['ũ'] = GenerateMap(_shadowEnglishFontAtlas1, 277, 405, 33, 45, 1024, 1024),
                            ['Ū'] = GenerateMap(_shadowEnglishFontAtlas1, 310, 405, 36, 45, 1024, 1024),
                            ['ū'] = GenerateMap(_shadowEnglishFontAtlas1, 346, 405, 33, 45, 1024, 1024),
                            ['Ŭ'] = GenerateMap(_shadowEnglishFontAtlas1, 379, 405, 36, 45, 1024, 1024),
                            ['ŭ'] = GenerateMap(_shadowEnglishFontAtlas1, 415, 405, 33, 45, 1024, 1024),
                            ['Ů'] = GenerateMap(_shadowEnglishFontAtlas1, 448, 405, 36, 45, 1024, 1024),
                            ['ů'] = GenerateMap(_shadowEnglishFontAtlas1, 484, 405, 33, 45, 1024, 1024),
                            ['Ű'] = GenerateMap(_shadowEnglishFontAtlas1, 517, 405, 36, 45, 1024, 1024),
                            ['ű'] = GenerateMap(_shadowEnglishFontAtlas1, 553, 405, 33, 45, 1024, 1024),
                            ['Ų'] = GenerateMap(_shadowEnglishFontAtlas1, 586, 405, 36, 45, 1024, 1024),
                            ['ų'] = GenerateMap(_shadowEnglishFontAtlas1, 622, 405, 33, 45, 1024, 1024),
                            ['Ŵ'] = GenerateMap(_shadowEnglishFontAtlas1, 655, 405, 47, 45, 1024, 1024),
                            ['ŵ'] = GenerateMap(_shadowEnglishFontAtlas1, 702, 405, 42, 45, 1024, 1024),
                            ['Ŷ'] = GenerateMap(_shadowEnglishFontAtlas1, 744, 405, 33, 45, 1024, 1024),
                            ['ŷ'] = GenerateMap(_shadowEnglishFontAtlas1, 777, 405, 33, 45, 1024, 1024),
                            ['Ÿ'] = GenerateMap(_shadowEnglishFontAtlas1, 810, 405, 33, 45, 1024, 1024),
                            ['Ź'] = GenerateMap(_shadowEnglishFontAtlas1, 843, 405, 35, 45, 1024, 1024),
                            ['ź'] = GenerateMap(_shadowEnglishFontAtlas1, 878, 405, 31, 45, 1024, 1024),
                            ['Ż'] = GenerateMap(_shadowEnglishFontAtlas1, 909, 405, 35, 45, 1024, 1024),
                            ['ż'] = GenerateMap(_shadowEnglishFontAtlas1, 944, 405, 31, 45, 1024, 1024),
                            ['Ž'] = GenerateMap(_shadowEnglishFontAtlas1, 975, 405, 35, 45, 1024, 1024),
                            ['ž'] = GenerateMap(_shadowEnglishFontAtlas1, 0, 450, 31, 45, 1024, 1024),
                            ['ƒ'] = GenerateMap(_shadowEnglishFontAtlas1, 31, 450, 35, 45, 1024, 1024),
                            ['Ș'] = GenerateMap(_shadowEnglishFontAtlas1, 66, 450, 37, 45, 1024, 1024),
                            ['ș'] = GenerateMap(_shadowEnglishFontAtlas1, 103, 450, 33, 45, 1024, 1024),
                            ['Ț'] = GenerateMap(_shadowEnglishFontAtlas1, 136, 450, 32, 45, 1024, 1024),
                            ['ț'] = GenerateMap(_shadowEnglishFontAtlas1, 168, 450, 25, 45, 1024, 1024),
                            ['ˆ'] = GenerateMap(_shadowEnglishFontAtlas1, 193, 450, 23, 45, 1024, 1024),
                            ['ˇ'] = GenerateMap(_shadowEnglishFontAtlas1, 216, 450, 23, 45, 1024, 1024),
                            ['ˉ'] = GenerateMap(_shadowEnglishFontAtlas1, 239, 450, 22, 45, 1024, 1024),
                            ['˘'] = GenerateMap(_shadowEnglishFontAtlas1, 261, 450, 23, 45, 1024, 1024),
                            ['˙'] = GenerateMap(_shadowEnglishFontAtlas1, 284, 450, 23, 45, 1024, 1024),
                            ['˚'] = GenerateMap(_shadowEnglishFontAtlas1, 307, 450, 23, 45, 1024, 1024),
                            ['˛'] = GenerateMap(_shadowEnglishFontAtlas1, 330, 450, 23, 45, 1024, 1024),
                            ['˜'] = GenerateMap(_shadowEnglishFontAtlas1, 353, 450, 23, 45, 1024, 1024),
                            ['˝'] = GenerateMap(_shadowEnglishFontAtlas1, 376, 450, 23, 45, 1024, 1024),
                            ['Ё'] = GenerateMap(_shadowEnglishFontAtlas1, 399, 450, 34, 45, 1024, 1024),
                            ['Ѓ'] = GenerateMap(_shadowEnglishFontAtlas1, 433, 450, 32, 45, 1024, 1024),
                            ['Є'] = GenerateMap(_shadowEnglishFontAtlas1, 465, 450, 34, 45, 1024, 1024),
                            ['Ѕ'] = GenerateMap(_shadowEnglishFontAtlas1, 499, 450, 37, 45, 1024, 1024),
                            ['І'] = GenerateMap(_shadowEnglishFontAtlas1, 536, 450, 24, 45, 1024, 1024),
                            ['Ї'] = GenerateMap(_shadowEnglishFontAtlas1, 560, 450, 24, 45, 1024, 1024),
                            ['Ј'] = GenerateMap(_shadowEnglishFontAtlas1, 584, 450, 31, 45, 1024, 1024),
                            ['Љ'] = GenerateMap(_shadowEnglishFontAtlas1, 615, 450, 43, 45, 1024, 1024),
                            ['Њ'] = GenerateMap(_shadowEnglishFontAtlas1, 658, 450, 37, 45, 1024, 1024),
                            ['Ќ'] = GenerateMap(_shadowEnglishFontAtlas1, 695, 450, 34, 45, 1024, 1024),
                            ['Ў'] = GenerateMap(_shadowEnglishFontAtlas1, 729, 450, 32, 45, 1024, 1024),
                            ['Џ'] = GenerateMap(_shadowEnglishFontAtlas1, 761, 450, 33, 45, 1024, 1024),
                            ['А'] = GenerateMap(_shadowEnglishFontAtlas1, 794, 450, 35, 45, 1024, 1024),
                            ['Б'] = GenerateMap(_shadowEnglishFontAtlas1, 829, 450, 35, 45, 1024, 1024),
                            ['В'] = GenerateMap(_shadowEnglishFontAtlas1, 864, 450, 35, 45, 1024, 1024),
                            ['Г'] = GenerateMap(_shadowEnglishFontAtlas1, 899, 450, 30, 45, 1024, 1024),
                            ['Д'] = GenerateMap(_shadowEnglishFontAtlas1, 929, 450, 35, 45, 1024, 1024),
                            ['Е'] = GenerateMap(_shadowEnglishFontAtlas1, 964, 450, 34, 45, 1024, 1024),
                            ['Ж'] = GenerateMap(_shadowEnglishFontAtlas1, 0, 495, 37, 45, 1024, 1024),
                            ['З'] = GenerateMap(_shadowEnglishFontAtlas1, 37, 495, 33, 45, 1024, 1024),
                            ['И'] = GenerateMap(_shadowEnglishFontAtlas1, 70, 495, 35, 45, 1024, 1024),
                            ['Й'] = GenerateMap(_shadowEnglishFontAtlas1, 105, 495, 34, 45, 1024, 1024),
                            ['К'] = GenerateMap(_shadowEnglishFontAtlas1, 139, 495, 33, 45, 1024, 1024),
                            ['Л'] = GenerateMap(_shadowEnglishFontAtlas1, 172, 495, 33, 45, 1024, 1024),
                            ['М'] = GenerateMap(_shadowEnglishFontAtlas1, 205, 495, 42, 45, 1024, 1024),
                            ['Н'] = GenerateMap(_shadowEnglishFontAtlas1, 247, 495, 33, 45, 1024, 1024),
                            ['О'] = GenerateMap(_shadowEnglishFontAtlas1, 280, 495, 35, 45, 1024, 1024),
                            ['П'] = GenerateMap(_shadowEnglishFontAtlas1, 315, 495, 34, 45, 1024, 1024),
                            ['Р'] = GenerateMap(_shadowEnglishFontAtlas1, 349, 495, 34, 45, 1024, 1024),
                            ['С'] = GenerateMap(_shadowEnglishFontAtlas1, 383, 495, 35, 45, 1024, 1024),
                            ['Т'] = GenerateMap(_shadowEnglishFontAtlas1, 418, 495, 35, 45, 1024, 1024),
                            ['У'] = GenerateMap(_shadowEnglishFontAtlas1, 453, 495, 35, 45, 1024, 1024),
                            ['Ф'] = GenerateMap(_shadowEnglishFontAtlas1, 488, 495, 36, 45, 1024, 1024),
                            ['Х'] = GenerateMap(_shadowEnglishFontAtlas1, 524, 495, 35, 45, 1024, 1024),
                            ['Ц'] = GenerateMap(_shadowEnglishFontAtlas1, 559, 495, 36, 45, 1024, 1024),
                            ['Ч'] = GenerateMap(_shadowEnglishFontAtlas1, 595, 495, 32, 45, 1024, 1024),
                            ['Ш'] = GenerateMap(_shadowEnglishFontAtlas1, 627, 495, 42, 45, 1024, 1024),
                            ['Щ'] = GenerateMap(_shadowEnglishFontAtlas1, 669, 495, 45, 45, 1024, 1024),
                            ['Ъ'] = GenerateMap(_shadowEnglishFontAtlas1, 714, 495, 35, 45, 1024, 1024),
                            ['Ы'] = GenerateMap(_shadowEnglishFontAtlas1, 749, 495, 40, 45, 1024, 1024),
                            ['Ь'] = GenerateMap(_shadowEnglishFontAtlas1, 789, 495, 34, 45, 1024, 1024),
                            ['Э'] = GenerateMap(_shadowEnglishFontAtlas1, 823, 495, 34, 45, 1024, 1024),
                            ['Ю'] = GenerateMap(_shadowEnglishFontAtlas1, 857, 495, 42, 45, 1024, 1024),
                            ['Я'] = GenerateMap(_shadowEnglishFontAtlas1, 899, 495, 35, 45, 1024, 1024),
                            ['а'] = GenerateMap(_shadowEnglishFontAtlas1, 934, 495, 32, 45, 1024, 1024),
                            ['б'] = GenerateMap(_shadowEnglishFontAtlas1, 966, 495, 33, 45, 1024, 1024),
                            ['в'] = GenerateMap(_shadowEnglishFontAtlas1, 0, 540, 31, 45, 1024, 1024),
                            ['г'] = GenerateMap(_shadowEnglishFontAtlas1, 31, 540, 30, 45, 1024, 1024),
                            ['д'] = GenerateMap(_shadowEnglishFontAtlas1, 61, 540, 33, 45, 1024, 1024),
                            ['е'] = GenerateMap(_shadowEnglishFontAtlas1, 94, 540, 33, 45, 1024, 1024),
                            ['ж'] = GenerateMap(_shadowEnglishFontAtlas1, 127, 540, 36, 45, 1024, 1024),
                            ['з'] = GenerateMap(_shadowEnglishFontAtlas1, 163, 540, 30, 45, 1024, 1024),
                            ['и'] = GenerateMap(_shadowEnglishFontAtlas1, 193, 540, 32, 45, 1024, 1024),
                            ['й'] = GenerateMap(_shadowEnglishFontAtlas1, 225, 540, 32, 45, 1024, 1024),
                            ['к'] = GenerateMap(_shadowEnglishFontAtlas1, 257, 540, 32, 45, 1024, 1024),
                            ['л'] = GenerateMap(_shadowEnglishFontAtlas1, 289, 540, 30, 45, 1024, 1024),
                            ['м'] = GenerateMap(_shadowEnglishFontAtlas1, 319, 540, 41, 45, 1024, 1024),
                            ['н'] = GenerateMap(_shadowEnglishFontAtlas1, 360, 540, 31, 45, 1024, 1024),
                            ['о'] = GenerateMap(_shadowEnglishFontAtlas1, 391, 540, 32, 45, 1024, 1024),
                            ['п'] = GenerateMap(_shadowEnglishFontAtlas1, 423, 540, 32, 45, 1024, 1024),
                            ['р'] = GenerateMap(_shadowEnglishFontAtlas1, 455, 540, 32, 45, 1024, 1024),
                            ['с'] = GenerateMap(_shadowEnglishFontAtlas1, 487, 540, 32, 45, 1024, 1024),
                            ['т'] = GenerateMap(_shadowEnglishFontAtlas1, 519, 540, 30, 45, 1024, 1024),
                            ['у'] = GenerateMap(_shadowEnglishFontAtlas1, 549, 540, 33, 45, 1024, 1024),
                            ['ф'] = GenerateMap(_shadowEnglishFontAtlas1, 582, 540, 37, 45, 1024, 1024),
                            ['х'] = GenerateMap(_shadowEnglishFontAtlas1, 619, 540, 31, 45, 1024, 1024),
                            ['ц'] = GenerateMap(_shadowEnglishFontAtlas1, 650, 540, 33, 45, 1024, 1024),
                            ['ч'] = GenerateMap(_shadowEnglishFontAtlas1, 683, 540, 31, 45, 1024, 1024),
                            ['ш'] = GenerateMap(_shadowEnglishFontAtlas1, 714, 540, 41, 45, 1024, 1024),
                            ['щ'] = GenerateMap(_shadowEnglishFontAtlas1, 755, 540, 42, 45, 1024, 1024),
                            ['ъ'] = GenerateMap(_shadowEnglishFontAtlas1, 797, 540, 31, 45, 1024, 1024),
                            ['ы'] = GenerateMap(_shadowEnglishFontAtlas1, 828, 540, 36, 45, 1024, 1024),
                            ['ь'] = GenerateMap(_shadowEnglishFontAtlas1, 864, 540, 31, 45, 1024, 1024),
                            ['э'] = GenerateMap(_shadowEnglishFontAtlas1, 895, 540, 30, 45, 1024, 1024),
                            ['ю'] = GenerateMap(_shadowEnglishFontAtlas1, 925, 540, 39, 45, 1024, 1024),
                            ['я'] = GenerateMap(_shadowEnglishFontAtlas1, 964, 540, 32, 45, 1024, 1024),
                            ['ё'] = GenerateMap(_shadowEnglishFontAtlas1, 0, 585, 33, 45, 1024, 1024),
                            ['ђ'] = GenerateMap(_shadowEnglishFontAtlas1, 33, 585, 33, 45, 1024, 1024),
                            ['ѓ'] = GenerateMap(_shadowEnglishFontAtlas1, 66, 585, 31, 45, 1024, 1024),
                            ['є'] = GenerateMap(_shadowEnglishFontAtlas1, 97, 585, 30, 45, 1024, 1024),
                            ['ѕ'] = GenerateMap(_shadowEnglishFontAtlas1, 127, 585, 32, 45, 1024, 1024),
                            ['і'] = GenerateMap(_shadowEnglishFontAtlas1, 159, 585, 23, 45, 1024, 1024),
                            ['ї'] = GenerateMap(_shadowEnglishFontAtlas1, 182, 585, 23, 45, 1024, 1024),
                            ['ј'] = GenerateMap(_shadowEnglishFontAtlas1, 205, 585, 22, 45, 1024, 1024),
                            ['љ'] = GenerateMap(_shadowEnglishFontAtlas1, 227, 585, 38, 45, 1024, 1024),
                            ['њ'] = GenerateMap(_shadowEnglishFontAtlas1, 265, 585, 41, 45, 1024, 1024),
                            ['ћ'] = GenerateMap(_shadowEnglishFontAtlas1, 306, 585, 33, 45, 1024, 1024),
                            ['ќ'] = GenerateMap(_shadowEnglishFontAtlas1, 339, 585, 31, 45, 1024, 1024),
                            ['ў'] = GenerateMap(_shadowEnglishFontAtlas1, 370, 585, 33, 45, 1024, 1024),
                            ['џ'] = GenerateMap(_shadowEnglishFontAtlas1, 403, 585, 33, 45, 1024, 1024),
                            ['Ґ'] = GenerateMap(_shadowEnglishFontAtlas1, 436, 585, 30, 45, 1024, 1024),
                            ['ґ'] = GenerateMap(_shadowEnglishFontAtlas1, 466, 585, 29, 45, 1024, 1024),
                            ['–'] = GenerateMap(_shadowEnglishFontAtlas1, 495, 585, 30, 45, 1024, 1024),
                            ['—'] = GenerateMap(_shadowEnglishFontAtlas1, 525, 585, 46, 45, 1024, 1024),
                            ['‘'] = GenerateMap(_shadowEnglishFontAtlas1, 571, 585, 22, 45, 1024, 1024),
                            ['’'] = GenerateMap(_shadowEnglishFontAtlas1, 593, 585, 22, 45, 1024, 1024),
                            ['‚'] = GenerateMap(_shadowEnglishFontAtlas1, 615, 585, 22, 45, 1024, 1024),
                            ['“'] = GenerateMap(_shadowEnglishFontAtlas1, 637, 585, 27, 45, 1024, 1024),
                            ['”'] = GenerateMap(_shadowEnglishFontAtlas1, 664, 585, 27, 45, 1024, 1024),
                            ['„'] = GenerateMap(_shadowEnglishFontAtlas1, 691, 585, 27, 45, 1024, 1024),
                            ['†'] = GenerateMap(_shadowEnglishFontAtlas1, 718, 585, 36, 45, 1024, 1024),
                            ['‡'] = GenerateMap(_shadowEnglishFontAtlas1, 754, 585, 36, 45, 1024, 1024),
                            ['•'] = GenerateMap(_shadowEnglishFontAtlas1, 790, 585, 31, 45, 1024, 1024),
                            ['…'] = GenerateMap(_shadowEnglishFontAtlas1, 821, 585, 47, 45, 1024, 1024),
                            ['‰'] = GenerateMap(_shadowEnglishFontAtlas1, 868, 585, 47, 45, 1024, 1024),
                            ['‹'] = GenerateMap(_shadowEnglishFontAtlas1, 915, 585, 24, 45, 1024, 1024),
                            ['›'] = GenerateMap(_shadowEnglishFontAtlas1, 939, 585, 24, 45, 1024, 1024),
                            ['€'] = GenerateMap(_shadowEnglishFontAtlas1, 963, 585, 35, 45, 1024, 1024),
                            ['™'] = GenerateMap(_shadowEnglishFontAtlas1, 0, 630, 46, 45, 1024, 1024),
                            ['−'] = GenerateMap(_shadowEnglishFontAtlas1, 46, 630, 34, 45, 1024, 1024),
                            ['∙'] = GenerateMap(_shadowEnglishFontAtlas1, 80, 630, 24, 45, 1024, 1024),
                        }
                    };
                    break;
            }
        }
    }
}
