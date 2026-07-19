# Dennoko PSD Editor User Guide

Dennoko PSD Editor is a tool that allows you to load PSD files directly in the Unity Editor, configure layer visibility and adjust color corrections, and export the final merged results as PNG, TGA, or PSD files.
Additionally, you can temporarily apply the compositing results in real-time to materials on 3D objects in the scene to inspect the visual output as you edit.

---

## Opening the Window & Header Features

From the Unity menu bar, select:

> **dennokoworks → Dennoko PSD Editor**

### Window Header Features

- **Title & Version Display**: Displays the window title and current version (e.g., `v1.x.x`) in the top left corner.
- **Manual Update Check Button (`↻`)**: Click the "`↻`" button next to the version label to manually re-check for the latest updates.
- **Language Toggle (`English`)**: Toggle the "English" checkbox in the top right corner to instantly switch between English and Japanese interfaces (saved in EditorPrefs).

---

## Loading PSD Files & History

There are several ways to load a PSD file into the editor.

### 1. Path Field & "Load" Button
1. Enter the path of the PSD file you want to load into the "PSD:" field at the top of the window.
   - Paths copied from Windows File Explorer (even with surrounding `"` quotes) will be automatically trimmed.
   - You can also click the "Browse..." button to select a file using the file selection dialog.
2. Click the "Load" button to analyze the PSD and display the layer list and preview.

### 2. Drag and Drop PSD Files
You can drag and drop a PSD file directly from Unity's Project view or Windows File Explorer into the window area to automatically populate the path and load the file.

### 3. History Menu (`History ▾` / `履歴 ▾`)
Clicking the "History ▾" button next to the "PSD:" label opens a dropdown list of recently loaded PSD file paths.
- Selecting any file from the list will instantly load that PSD.
- Missing/deleted files will be grayed out in the list.
- Select "Clear History" (履歴をクリア) at the bottom of the dropdown to reset the history list.

---

## Output / Export Path Settings

Specify the export destination folder in the "Export Dir:" field at the top of the window.

- By default, it is set to `Assets/DennokoPSDEditor_exported`.
- You can specify either a relative path starting with `Assets/...` or an absolute PC path.
- You can also click the "Browse..." button to select a folder via dialog (selecting a folder inside the project automatically converts it to an `Assets/...` relative path).

---

## Material Preview (Real-Time 3D Reflection)

You can temporarily apply the live PSD composition result to a material assigned to a 3D object in the scene or an asset in your project. This allows you to inspect color adjustments and layer changes on a 3D model in real-time.

- **Material Preview**: Drag and drop or assign a target material (from a Scene object or Project asset) to the object field.
- **Texture (Slot Name)**: Specify the texture property name on the material to replace (default: `_MainTex`).
  - Clicking the "`▾`" dropdown button on the right lists all available texture properties on the selected material's shader (e.g., `_MainTex`, `_BumpMap`).
- **Preview / Previewing Button**: 
  - Assigning a material automatically enables real-time preview, changing the button label to "Previewing".
  - Click the button to toggle live preview ON or OFF at any time.
  - Disabling preview or clearing the material field automatically restores the material to its original texture.
  - Closing the window or triggering a domain reload (e.g., script recompilation) will also safely restore the original texture.

---

## Layer Panel (Left Side of the Screen)

The PSD file's layers and groups are displayed in a tree structure. The topmost layer is displayed at the top.

### Basic Controls

| Action | Description |
|------|------|
| Checkbox | Toggle layer/group visibility |
| Foldout Arrow | Expand or collapse group contents (clicking the arrow toggles expand/collapse without changing selection) |
| Row Click | Select a layer or group |
| Dropdown (Far Right) | Change blend mode (PassThrough is also available for groups) |

### Multi-Selection & Batch Editing

Using keyboard modifiers, you can select multiple layers or groups simultaneously and edit them in batches.

- **Individual Toggle Selection**: Hold `Ctrl` (Mac: `Cmd`) and click layers to toggle individual selection state on/off.
- **Range Selection**: Hold `Shift` and click another layer to select all layers between the initial selection and the clicked layer.
- **Deselect All**: Press the `Escape` key or click anywhere on the empty workspace area in the layer panel to clear selection.

#### Batch Editing Features
When multiple layers are selected, performing any of the following operations will **apply the change simultaneously across all selected layers**:
- Toggling visibility checkboxes
- Adjusting Opacity sliders
- Changing Blend Mode dropdowns
- Modifying Color Correction parameters (Brightness/Contrast, Hue/Saturation, Tone Curves, Gradient Maps, etc.)

---

### Color Correction Context Menu (Right-Click)

Right-clicking the "Color Correction" header or any of the adjustment control areas on a layer or group opens a context menu.

- **Copy Adjustment**: Copies all current color correction parameters of that layer/group to the internal clipboard.
- **Paste Adjustment**: Pastes the copied color correction parameters onto the selected layer or group (applies to all selected layers if multiple layers are selected).
- **Reset Adjustment**: Resets all color correction parameters on that layer/group back to their default values (no effect).

---

### Common Controls (Displayed when expanding visible layers/groups)

#### Opacity
Adjust within the range of 0.0 to 1.0 using the slider (supports batch editing for multi-selections).

#### Group (Folder) Color Corrections & Masks
Folders also feature "Color Correction" and "Mask Generation" foldouts, allowing you to apply adjustments or export masks to the flattened result of the folder's contents.

* Color correction options for folders are identical to pixel layers (except Image Clip Blend and Luminance Normalization).
* Applying adjustments to a PassThrough folder switches its blending to Isolated Blend (equivalent to Normal) while adjustments are active.
* When exporting to PSD format, folder adjustments are saved as an **unclipped adjustment layer at the top of the folder** (with `dPSE` metadata). Re-importing into this tool automatically restores them as folder adjustment parameters.
* PassThrough folders containing active adjustments will be **exported as Normal blend folders** to prevent adjustments from bleeding outside the group (the export confirmation dialog will notify you of the count).

---

### Pixel Layer Controls

#### Color Correction Foldout
Expanding "Color Correction" displays various sliders and functions for adjusting brightness and color tones. Modifications made here are "non-destructive" and do not affect the original PSD, allowing you to freely experiment and adjust them as many times as you like (available for any pixel layer).

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

#### Mask Generation
Expanding the "Mask Generation" foldout lets you export mask PNG images based on the pixels of this layer or folder.

##### Color Range Mask
Expanding "Color Range Mask" creates a selection area based on a specified target color and fuzziness threshold, exporting it as a grayscale PNG (similar to Photoshop's Color Range feature).

- **Target Color**: Set directly via the color picker, or turn on the "Eyedropper" button and click the preview to pick a color.
  - The eyedropper picks directly from **this layer's raw pixels** (or the flattened folder contents), not from the final composited image.
  - Clicking outside the layer boundaries will not pick a color. Picking a color automatically deactivates the eyedropper.
- **Fuzziness (Threshold)**: Set from 0.0 to 1.0 to determine how close colors must be to the Target Color to be selected.
- **Highlight Preview**: Modifying the Target Color or Fuzziness dynamically highlights the selected area in red on the preview.
- **"Stop Preview" Button**: Turns off the red highlight preview.
- **"Export Mask as PNG" Button**: Exports a grayscale PNG where selected areas are white and unselected/transparent areas are black. Saved to the "Export Dir" as `<PSD Name>_<Layer Name>_mask.png`.

##### Opacity Range Mask
- **"Export Opacity Mask as PNG" Button**: Exports a grayscale PNG where opaque areas are white and transparent areas are black. Saved as `<PSD Name>_<Layer Name>_opacity_mask.png`.

---

### Adjustment Layer and Solid Color Layer Controls

Adjustment layers and Solid Color (Fill) layers created in Photoshop are automatically recognized when importing a PSD file. Selecting one of these layers displays its dedicated controls in the panel, allowing you to edit the settings non-destructively.

Supported adjustment layer types include:
- **Brightness & Contrast / Hue, Saturation & Lightness**
- **Invert / Threshold / Posterization / Color Balance / Levels / Curves**
- **Gradient Map**
- **Solid Color (SoCo)**

---

### Panel Width Adjustment

You can adjust panel widths by dragging the vertical splitter line between the layer panel and preview panel left or right.

---

## Preview (Right Side of the Screen)

- Changes made in the layer panel are composited and displayed in real-time.
- Transparent areas are represented by a checkerboard pattern.
- Turning on the "Merged Ref" toggle displays Photoshop's original saved merged image as a thumbnail overlay in the bottom right of the preview for comparison.

---

## Status Bar and Exporting (Bottom of the Screen)

At the very bottom of the window, metadata for the loaded PSD is displayed ("Width × Height", "Layer Count", "Bit Depth", "Color Mode"), alongside status messages.

### Exporting Images and PSDs

Select the output format (PNG / PSD / TGA) on the right side of the bottom bar and click the "Export" button.

* **PNG**: Exports the current preview composition as a PNG image.
* **PSD**: Exports a new PSD file preserving layer visibility and non-destructive adjustments.
* **TGA**: Exports the current preview composition as a 32-bit TGA image (with alpha).

#### How Adjustments are Saved in PSD Exports

Non-destructive color corrections applied to pixel layers are written as **Adjustment Layers** clipped to the target layer (embedding internal identification key `dPSE`).

* The original layer pixel data remains intact.
* Re-importing the exported PSD in this tool automatically restores adjustment parameters onto the base layers.
* External editors like Photoshop or Clip Studio Paint recognize them as standard clipped adjustment layers.

---

## Limitations & Known Issues when Importing PSDs

This tool uses a custom high-speed binary parser rather than external Photoshop libraries. Certain Photoshop features may have limitations:

### 1. Supported File Formats & Versions
- **PSB Format Not Supported**: PSB files (>30,000 px or >2GB) cannot be loaded. Save as PSD format instead.
- **32-bit/Channel Not Supported**: Convert 32-bit HDR PSD files to 8-bit or 16-bit before loading.
- **Color Modes**: Individual layer parsing is supported for **RGB** and **Grayscale** modes. CMYK or Lab files will show a warning and load only the merged composite image.

### 2. Rendering Accuracy & Color Spaces
- **16-bit Channel Downsampling**: 16-bit PSD files are automatically downsampled to 8-bit upon loading.
- **Color Profiles Ignored**: Embedded color profiles are ignored and raw pixel data is treated as sRGB-equivalent.

### 3. Unsupported Features
- Unsupported adjustment layer types (Exposure, Black & White, Photo Filter, etc.) load as empty transparent layers.
- Layer Styles: Only "Color Overlay" is supported; others are ignored.
- PassThrough groups ignore folder-level masks and opacity settings.
