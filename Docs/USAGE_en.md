# Dennoko PSD Editor User Guide

Dennoko PSD Editor is a tool that allows you to load PSD files directly in the Unity Editor, configure layer visibility and adjust color corrections, and export the final merged results as PNG, TGA, or PSD files.

---

## How to Open the Window

From the Unity menu bar, select:

> **dennokoworks → Dennoko PSD Editor**

---

## Loading PSD Files

1. Enter the path of the PSD file you want to load into the "PSD:" field at the top of the window.
   - Paths copied from Windows File Explorer (even with surrounding `"` quotes) will be automatically trimmed.
   - You can also click the "Browse..." button to select a file using the file dialog.
2. Click the "Load" button to analyze the PSD, displaying the layer list and preview.

---

## Output / Export Path Settings

Specify the export destination folder in the "Export Dir:" field at the top of the window.

- By default, it is set to `Assets/DennokoPSDEditor_exported`.
- You can specify either a relative path starting with `Assets/...` or an absolute PC path.
- You can also click the "Browse..." button to specify a folder using the folder selection dialog.

---

## Layer Panel (Left Side of the Screen)

The PSD file's layers and groups are displayed in a tree structure. The topmost layer is displayed at the top.

### Basic Controls

| Action | Description |
|------|------|
| Checkbox | Toggle layer/group visibility |
| Foldout Arrow | Expand or collapse the layers inside a group |
| Dropdown (Far Right) | Change blend mode (PassThrough is also available for groups) |

### Common Controls (Displayed when expanding visible layers/groups)

#### Opacity
Adjust within the range of 0.0 to 1.0 using the slider.

---

### Pixel Layer Controls

#### Color Correction Foldout
Expanding "Color Correction" (色調補正) displays various sliders and functions for adjusting brightness and color tones. Modifications made here are "non-destructive" and do not affect the original PSD, allowing you to freely experiment and adjust them as many times as you like (available for any pixel layer).

Adjusting the sliders will reflect the changes in the preview in real-time. Returning the values to their default states (center or default value) will disable the corresponding effect.

##### Basic Color Correction
* **Brightness**: Brightens or darkens the entire image (-150 to 150. 0 means no change).
* **Contrast**: Increases or decreases the difference between light and dark areas (-50 to 100. 0 means no change).
* **Hue**: Rotates the colors to change them to different hues (-180 to 180 degrees. 0 means no change).
* **Saturation**: Adjusts the vividness of colors (-100 to 100. Negative values bring the colors closer to grayscale/monochrome).
* **Lightness**: Adjusts the overall lightness (-100 to 100. 0 means no change).
* **Colorize (Apply color to grayscale/monochrome areas)**: Normally, Hue and Saturation adjustments do not affect grayscale parts (white, black, gray). Enabling this option allows you to colorize those monochrome areas.

##### Other Filters and Adjustments
* **Invert**: Inverts the colors, resulting in a look similar to a photo negative.
* **Threshold**: Converts the image into binary black and white. The "Threshold Level" (0 to 255) determines the boundary brightness at which pixels are separated into black and white.
* **Posterization**: Reduces the number of color tones (levels) to create a stylized, high-contrast poster or illustration effect. The smaller the "Levels" (2 to 255) value, the stronger the effect.
* **Color Balance**: Adjusts colors in "Shadows" (dark areas), "Midtones", and "Highlights" (bright areas). Each can be adjusted using three sliders (Cyan ↔ Red, Magenta ↔ Green, Yellow ↔ Blue) ranging from -100 to 100. Turning on "Preserve Luminosity" prevents the overall brightness from changing when colors are adjusted.
* **Levels**: Adjusts contrast and tone by shifting the boundaries for dark areas (Input Shadow) and bright areas (Input Highlight). You can also adjust middle-tone brightness with "Gamma", and the final output brightness range using "Output Shadow" and "Output Highlight".
* **Curves (Tone Curve)**: Adjusts output brightness by dragging points on the graph directly. Pulling the curve up brightens the image, and pulling it down darkens it.

#### Gradient Map
Enable this by checking the "Gradient Map" checkbox. This applies gradient colors according to the brightness (luminance) of the layer.

- **Gradient**: Click this to open Unity's Gradient Editor and customize your color palette.
- **Normalize Luminance**: Checking this stretches the luminance range of the darkest to brightest colors in the layer to 0–1, ensuring the gradient is applied evenly.
- **Factor (Apply Ratio)**: Adjusts the blending strength of the gradient map from 0.0 to 1.0.

#### Image Clip Blend
Enabling "Image Clip Blend" allows you to blend any texture/image using the alpha channel outline (mask) of this layer.

- **Image**: Select the texture to use from your project assets.
- **Tiling**: Specify the repeating tile count for X and Y directions (1.0 = no tiling).
- **Blend Mode**: Select the blend mode used to combine the image.
- **Opacity**: Adjust the blend intensity from 0.0 to 1.0.

#### Color Range Mask
Expanding the "Color Range Mask" foldout lets you create a selection area based on a specified color and threshold, which can then be exported as a mask image (PNG). This functions similarly to Photoshop's "Color Range" command.

- **Target Color**: Specify directly using the color picker, or turn on the "Eyedropper" button and click on the preview area to pick a color.
  - The eyedropper picks the color directly from **this layer's raw pixels**, not from the final merged/composited preview.
  - Clicking outside the boundary of this layer will not register a color. Once a color is picked, the eyedropper is automatically deactivated.
- **Fuzziness (Threshold)**: Set from 0.0 to 1.0. This determines how close colors must be to the Target Color to be selected. A value of 0 requires an exact match, while higher values select a wider range.
- **Highlight Preview**: Changing the Target Color or Fuzziness dynamically highlights the selected area in red on the preview. This highlight also appears automatically when picking a color with the eyedropper.
- **"Stop Preview" Button**: Turns off the red highlight preview (only clickable while the highlight preview is active).
- **"Export Mask as PNG" Button**: Exports a grayscale PNG where the selected area is white and all other areas (including transparent pixels) are black. The output file is saved to the "Export Dir" folder with the name format `<PSD Name>_<Layer Name>_mask.png`. If saved inside the Assets folder, it will be automatically highlighted in Unity's Project window.

---

### Adjustment Layer and Solid Color Layer Controls

Adjustment layers and Solid Color (Fill) layers created in Photoshop are automatically recognized when importing a PSD file. Selecting one of these layers displays its dedicated controls in the panel, allowing you to edit the settings non-destructively. The values configured in Photoshop are carried over.

The following adjustment layers are automatically recognized:

- **Brightness & Contrast / Hue, Saturation & Lightness**: Adjust using the sliders and the "Colorize" checkbox.
- **Invert / Threshold / Posterization / Color Balance / Levels / Curves**: The respective sliders and curve editors will be displayed in the panel for editing.
- **Gradient Map**: The gradient settings set in Photoshop are imported, and you can edit the gradient keys and blending factor (see the "Gradient Map" section above for details).
- **Solid Color (SoCo)**: Directly change the fill color using the color picker field.

---

### Panel Width Adjustment

You can adjust the width of the panels by dragging the vertical line (splitter) between the layer panel and the preview panel to the left or right.

---

## Preview (Right Side of the Screen)

- Changes made in the layer panel (visibility, opacity, blend modes, color corrections, etc.) are merged and reflected in the preview area in real-time.
- Transparent areas are represented by a checkerboard pattern.
- Turning on the "Merged Ref" toggle displays the merged image stored when the PSD was saved in Photoshop as a small thumbnail overlay in the bottom right of the preview. This is useful for comparing the real-time composition results with the original Photoshop export.

---

## Status Bar and Exporting (Bottom of the Screen)

At the very bottom of the window, the loaded PSD file's metadata is displayed: "Width × Height", "Number of Layers", "Bit Depth", and "Color Mode".

### Exporting Images and PSDs

Select the output format (PNG / PSD / TGA) on the right side of the bottom bar and click the "Export" button to save it to the specified "Export Dir" folder.

* **PNG**: Exports the current preview composition as a PNG image.
* **PSD**: Exports a new PSD file with the current layer visibility and adjustment parameters preserved. A file save dialog will open.
* **TGA**: Exports the current preview composition as a 32-bit TGA image (with alpha).

#### How Adjustments are Saved in PSD Exports

Non-destructive color corrections applied to pixel layers (Brightness & Contrast, Hue & Saturation (including colorize), Invert, Threshold, Posterization, Levels, Curves, Color Balance, and Gradient Maps) are not baked into the pixels. Instead, they are written as **Adjustment Layers** clipped to the target layer. As a result:

* The original layer images are preserved without modifications.
* Re-importing the exported PSD in this tool automatically restores the adjustment parameters to the layer's properties, allowing you to continue editing where you left off.
* External editors like Photoshop or Clip Studio Paint will recognize and let you edit them as standard clipped adjustment layers (visual output might differ slightly as different editors use slightly different rendering formulas).

The following exceptions are baked into the pixels as before (the count of baked adjustments will be displayed upon export):

* Adjustments applied to layers that are already clipping masks.
* Adjustments on layers where "Blend Clipped Layers as Group" is disabled.
* Gradient maps with "Normalize Luminance" enabled (as there is no native equivalent in standard PSD files).

Additionally, layers with a "Color Overlay" effect are converted and written as solid color clipping layers (any color overlays applied to clipping layers will be lost). Image clip blends are written as independent clipping layers as before.

#### Note on Re-importing PSDs in This Tool

* Exported PSDs contain identification metadata (internal key `dPSE`) indicating they were created by this tool. Upon re-import, this metadata is detected, and the adjustment layers are **not shown** in the layer list; instead, they are automatically restored as adjustment parameters on their respective base layers. It is expected that the state returns to exactly how it was before exporting.
* However, if the exported PSD is **edited and saved in other software** before loading it back:
  * Adjustment layers will not be merged back into the base layer properties if they had masks added, were hidden, or had their blend modes/opacity changed in the external software (this is a safety mechanism to prevent losing those edits). They will appear as standard clipped adjustment layers.
  * **Generally any PSD overwritten in Photoshop or other editors** will lose the `dPSE` metadata, as external tools discard unknown metadata keys. The visual appearance and parameters are preserved, but they will load as independent adjustment layers instead of being absorbed back into the base layer's properties.
* Layers that were baked (e.g., adjustments on clipping layers) have had their pixels permanently altered, so their settings cannot be reverted or changed after re-importing.
* Curves are simplified to the PSD spec limit (maximum 19 points), and gradients are simplified to Unity's limit (maximum 8 color keys, 8 alpha keys). Configurations with many control points may be slightly simplified after round-tripping.

#### Note on Opening Exports in External Software

* Exported adjustments are recognized as standard **clipped adjustment layers** in Photoshop, Clip Studio Paint, Krita, etc., and can be edited. They are given fixed names such as "Brightness/Contrast" or "Hue/Saturation".
* Since external software uses different blending and correction math compared to this tool's shader implementation, **the visual appearance may differ slightly**. This is particularly common with Brightness/Contrast, Hue/Saturation, and Gradient Maps (they will match perfectly when re-imported back into this tool).
* The "Factor (Apply Ratio)" of a Gradient Map is exported as the adjustment layer's **Opacity** in the PSD. You can adjust the intensity in external software by changing the layer's opacity.
* Selective color range adjustments within Hue/Saturation (e.g., modifying only Reds or Yellows) are not exported (only the master adjustments are written).
* The `dPSE` metadata key is ignored by external software but complies with PSD specifications, so it will not cause file errors.
* Because adjustment layers are exported clipped to the base layer, releasing the clipping mask in external software will apply the adjustments to all layers below it (changing the visual appearance from how it looked in this tool).

* If saved inside the Unity project (under `Assets`), the file is automatically detected and highlighted in the Project view.
* If saved outside the project, the target folder will open in Windows File Explorer.

---

## Limitations & Known Issues when Importing PSDs

This tool imports PSD files using a custom, high-speed binary parser rather than relying on external Photoshop libraries. As a result, certain Photoshop features and data may not be perfectly recreated or may have limitations during preview or re-export.

### 1. Supported File Formats & Versions
- **PSB Format (Large Document Format) Not Supported**: PSB files (used for images larger than 30,000 pixels or exceeding 2GB) cannot be loaded and will cause an error. Please save the file in PSD format.
- **32-bit/Channel Not Supported**: PSD files with 32-bit depth per channel (e.g., HDR) cannot be loaded. Convert the image to 8-bit or 16-bit before saving.
- **Color Modes**: Layers can only be parsed and recreated for **RGB** and **Grayscale** modes. Importing PSDs saved in CMYK, Lab, or other color modes will trigger a warning, and only the pre-merged "Composite Image" (flattened image) will be loaded for preview without individual layers.

### 2. Rendering Accuracy & Color Spaces
- **16-bit Channel Downsampling**: 16-bit PSD files are automatically downsampled (converted) to 8-bit when loaded. This may lead to slight precision loss in fine gradients or color depth.
- **Color Profiles Ignored**: Embedded color profiles (such as Adobe RGB or Display P3) are ignored, and the raw pixel values are interpreted as sRGB-equivalent. The colors may not exactly match the original appearance in Photoshop.
- **CMYK Composite Approximation**: When loading the merged composite image of a CMYK PSD, a simple RGB conversion formula is applied. The colors may differ slightly from Photoshop's native RGB conversion.

### 3. Unsupported Features
- **Adjustment Layer Limitations**:
  - The following adjustment layers are supported for import (the text in parentheses refers to internal PSD keys for reference):
    - Brightness/Contrast (`brit` / `CgEd`)
    - Hue/Saturation/Lightness (`hue2`)
    - Invert (`nvrt` / `invr`)
    - Threshold (`thrs`)
    - Posterize (`post`)
    - Color Balance (`blnc`)
    - Levels (`levl`)
    - Curves (`curv`)
    - Gradient Map (`grdm`)
    - Solid Color (`SoCo` *RGB only)
  - Other adjustment layers (e.g., Exposure, Black & White, Photo Filter, Color Lookup, etc.) are not supported. Their settings will be ignored, and they will load as empty transparent layers.
- **Layer Effects (Layer Styles)**:
  - Only "Color Overlay" is supported.
  - Drop Shadow, Stroke, Gradient Overlay, Outer Glow, and all other layer styles are ignored.
- **Mask Limitations**:
  - "Vector Masks" and "Global Layer Masks" on layers are skipped (only standard "Layer Masks" are supported).
- **PassThrough Groups Simplification**:
  - If a group's blend mode is set to "PassThrough", any masks or opacity settings applied to the group folder itself are ignored.
  - Clipping layers positioned directly above a PassThrough group will be displayed without clipping because a precise clipping mask shape cannot be generated for PassThrough groups.
- **Rasterization of Special Layers**:
  - Text layers, Shape layers, and Smart Objects do not have their vector/text edit data parsed. Instead, they are loaded statically using the "rasterized pixel image" cached inside the PSD when saved (requires the "Maximize Compatibility" option to be enabled when saving the PSD in Photoshop).
- **Dissolve Blend Mode Approximation**:
  - The "Dissolve" blend mode is approximated using screen-resolution-dependent hash noise. The noise pattern may look different from Photoshop depending on the zoom level.
