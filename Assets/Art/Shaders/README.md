# Procedural Landscape Shader

A configurable URP-compatible terrain shader for Spellbreakers with heightmap-based biome blending, vertex paint support, and slope-based blending.

## Features

- **6 Biome Layers**: Water → Sand → Grass → Dirt → Rock → Snow
- **Heightmap-Driven**: Uses a heightmap texture (R channel) for elevation
- **Configurable Elevation Ranges**: Set min/max height and blend width per biome
- **Vertex Paint Support**: Use vertex colors (R,G,B,A) for manual biome control
- **Slope Blending**: Automatically adds rock/dirt on steep slopes
- **Noise Variation**: Natural biome boundary variation via noise texture
- **Detail Maps**: High-frequency detail at close range
- **Distance Fading**: Performance optimization for far terrain
- **URP Compatible**: Works with Universal Render Pipeline

## Quick Start

### 1. Apply to ValleyGround

Use the menu: **Tools → Spellbreakers → Quick Setup ValleyGround Landscape**

This will:
- Find ValleyGround in the scene
- Create a material instance with Temperate Mountains preset
- Apply to all renderers
- Generate heightmaps from mesh geometry

### 2. Customize Biomes

Select the created material (`ValleyGround_Landscape.mat`) in the Project window to access the custom inspector with organized sections:

- **Base Maps** - Albedo, Normal, Heightmap, Occlusion
- **Elevation Settings** - World-space height range
- **Biome Layers** - 6 layers with textures, normals, height ranges, tiling, smoothness, metallic
- **Vertex Paint** - Enable/disable vertex color blending
- **Slope Blending** - Steep slope rock/dirt blending
- **Noise Variation** - Natural boundary variation
- **Detail Maps** - Close-up detail
- **Global Settings** - Overall smoothness/metallic
- **Advanced** - Tessellation, distance fade, standard URP settings

### 3. Biome Presets

Right-click in Project window → **Create → Spellbreakers → Biome Preset** for:
- Temperate Mountains
- Arctic Peaks
- Desert Canyons
- Volcanic Wasteland
- Lush Valley

## Shader Properties

### Elevation Mapping
```
Heightmap (0-1) × ElevationScale = World Height
Biomes blend based on Min/Max Height in world units
```

### Vertex Paint Channels
- **R** = Layer 0 (Water/Deep)
- **G** = Layer 1 (Sand/Shore)
- **B** = Layer 2 (Grass/Lowlands)
- **A** = Layer 3 (Dirt/Hills)

### Layer Configuration (per layer)
| Property | Description |
|----------|-------------|
| Albedo | Base color texture |
| Normal | Normal map texture |
| Min Height | World height where layer starts |
| Max Height | World height where layer ends |
| Blend Width | Transition smoothness |
| Tiling | Texture tiling scale |
| Smoothness | Surface smoothness (0-1) |
| Metallic | Metallic factor (0-1) |

## Heightmap Requirements

- **Format**: RFloat or RGBA32 (uses R channel)
- **Resolution**: 1024×1024 recommended
- **Values**: 0-1 mapped to ElevationMin-ElevationMax
- **Wrap Mode**: Clamp
- **Filter Mode**: Bilinear

## Vertex Paint Workflow

1. Model terrain in Blender/other DCC
2. Paint vertex colors for biome control
3. Import with "Import Vertex Colors" enabled
4. Enable `_UseVertexPaint` on material
5. Adjust `_VertexPaintStrength` for blend amount

## Performance Tips

- Use **Distance Fading** for large terrains
- Lower **Detail Strength** for better performance
- Disable **Slope Blending** if not needed
- Consider **Tessellation** only for close-up hero terrain

## Custom Biome Setup

To create custom biomes, adjust the 6 layer height ranges:

```
Layer 0: _Layer0Min → _Layer0Max (e.g., -10 to 0 = Water)
Layer 1: _Layer1Min → _Layer1Max (e.g., 0 to 5 = Sand)
Layer 2: _Layer2Min → _Layer2Max (e.g., 5 to 25 = Grass)
Layer 3: _Layer3Min → _Layer3Max (e.g., 25 to 50 = Dirt)
Layer 4: _Layer4Min → _Layer4Max (e.g., 50 to 80 = Rock)
Layer 5: _Layer5Min → _Layer5Max (e.g., 80 to 120 = Snow)
```

Ensure layers overlap slightly (via Blend Width) for smooth transitions.

## Shader Keywords

The editor automatically manages these keywords:
- `_HEIGHTMAP` - Heightmap assigned
- `_NORMALMAP` - Base normal map assigned
- `_DETAIL_NORMAL` - Detail normal assigned
- `_VERTEXPAINT` - Vertex paint enabled
- `_SLOPE_BLEND` - Slope blending enabled
- `_DISTANCE_FADE` - Distance fading enabled
- `_ALPHATEST_ON` - Alpha clipping enabled

## Files

| File | Description |
|------|-------------|
| `ProceduralLandscape.shader` | Main shader |
| `ProceduralLandscape.mat` | Base material |
| `ProceduralLandscapeEditor.cs` | Custom material inspector |
| `BiomePresets.cs` | Right-click biome presets |
| `QuickSetupLandscape.cs` | One-click ValleyGround setup |
| `ProceduralLandscapeSetup.cs` | Advanced setup window |

## Troubleshooting

**Shader shows pink/missing:**
- Check URP version compatibility (tested on 17.4.0)
- Ensure all `#include` paths are correct
- Check Console for shader errors

**Heightmap not working:**
- Verify texture format (RFloat or RGBA32)
- Check `_HeightMap` is assigned in material
- Ensure `_ElevationScale` > 0

**Biomes not blending:**
- Check layer Min/Max heights are in correct order
- Ensure Blend Width > 0
- Verify ElevationMin/Max encompasses all layers

**Vertex paint not showing:**
- Mesh must have vertex colors
- Import model with "Import Vertex Colors" checked
- `_UseVertexPaint` must be 1
- `_VertexPaintStrength` > 0