using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace PSDSimpleEditor
{
    /// <summary>
    /// PSD (version 1) バイナリ → PSDFile 変換パーサー。
    ///
    /// 設計原則 (REWRITE_SPEC.md §0 / §5):
    ///   - すべての可変長ブロックは「end = pos + len を先に計算 → 処理 → 必ず Seek(end)」で
    ///     境界管理する。ブロック内のパース失敗が後続データのストリーム位置を壊さない。
    ///   - 致命的エラー (シグネチャ不正 / version!=1 / depth==32) のみ例外。
    ///     レイヤー単位・ブロック単位の失敗は Debug.LogWarning + スキップで続行。
    ///
    /// 対応機能:
    ///   - 8bit / 16bit (16bit は上位バイト採用で 8bit 化。Lr16 ブロック対応)
    ///   - RGB / Grayscale (R 複製)。CMYK / LAB は警告 + マージ画像のみ
    ///   - 圧縮: Raw / RLE (PackBits) / ZIP / ZIP+prediction
    ///   - luni (UTF-16BE レイヤー名・日本語対応) / lsct・lsdk (グループ) / クリッピング
    ///   - レイヤーマスク (相対座標 → 絶対座標変換、id==-2 はマスク矩形で読む)
    ///   - 調整: brit / CgEd / hue2 / SoCo (最小ディスクリプタパーサー使用)
    ///   - lfx2 / lrFX の Color Overlay (best effort)
    ///
    /// 実装は責務ごとに Core/Parser/ 配下のクラスへ分割されている
    /// (PSDHeaderReader / PSDLayerRecordParser / PSDAdditionalInfoParser / PSDDescriptorParser /
    ///  PSDChannelDecoder / PSDLayerAssembler / PSDMergedImageParser / PSDBlendModeConverter)。
    /// このクラスは公開 API とパース全体のオーケストレーションのみを担う。
    /// </summary>
    public static class PSDParser
    {
        // ════════════════════════════════════════════════════════════════
        //  公開 API
        // ════════════════════════════════════════════════════════════════

        /// <summary>true にすると Parse(path) でも詳細ダンプログを出力する。</summary>
        public static bool VerboseLog = false;

        /// <summary>PSD ファイルをパースする (既存呼び出し互換)。</summary>
        public static PSDFile Parse(string filePath)
        {
            return Parse(filePath, VerboseLog);
        }

        /// <summary>PSD ファイルをパースする。verbose=true でレイヤー構造のダンプログを出力。</summary>
        public static PSDFile Parse(string filePath, bool verbose)
        {
            // 直接アクセスが失敗した場合はダブルクォーテーションを無視して再アクセス
            if (!string.IsNullOrEmpty(filePath) && !File.Exists(filePath))
            {
                if (filePath.StartsWith("\"") || filePath.EndsWith("\""))
                {
                    string trimmed = filePath.Trim('"');
                    if (File.Exists(trimmed))
                    {
                        filePath = trimmed;
                    }
                }
            }

            var psd  = new PSDFile();
            var vlog = verbose ? new StringBuilder() : null;
            vlog?.AppendLine($"[PSDParser] ダンプ: {filePath}");

            List<PSDLayer> flat;
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (var r  = new BigEndianBinaryReader(fs))
            {
                // Section 1: ヘッダ (致命的エラーはここで throw)
                PSDHeaderReader.ParseHeader(r, psd, vlog);

                // Section 2: カラーモードデータ / Section 3: イメージリソース → 長さを読んでスキップ
                PSDHeaderReader.SkipLengthPrefixedBlock(r);
                PSDHeaderReader.SkipLengthPrefixedBlock(r);

                // Section 4: レイヤーとマスク情報
                flat = PSDLayerRecordParser.ParseLayerAndMaskSection(r, psd, vlog);

                // Section 5: マージ済み画像 (失敗しても警告のみで続行)
                PSDMergedImageParser.ParseMergedImage(r, psd, vlog);
            }

            // テクスチャ構築 (構築後 _rawPixels は null 化)
            PSDLayerAssembler.BuildLayerTextures(flat);

            // グループツリー構築 (index 0 = 最下層)
            psd.Layers = PSDLayerAssembler.BuildLayerTree(flat, vlog);

            // UI 初期値の設定 (パーサーの責務)
            PSDLayerAssembler.InitUIState(psd.Layers);

            if (vlog != null) Debug.Log(vlog.ToString());
            return psd;
        }

        // ════════════════════════════════════════════════════════════════
        //  デバッグダンプ
        // ════════════════════════════════════════════════════════════════

        internal static void DumpLayer(StringBuilder vlog, int index, PSDLayer layer)
        {
            if (vlog == null) return;
            string mask = layer.HasMask
                ? $"({layer.MaskLeft},{layer.MaskTop},{layer.MaskRight},{layer.MaskBottom}) def={layer.MaskDefaultColor}{(layer.MaskIsDisabled ? " 無効" : "")}"
                : "-";
            string adj = "";
            if (layer.Adjustment.HasBrightnessContrast) adj += $" brit({layer.Adjustment.Brightness},{layer.Adjustment.Contrast})";
            if (layer.Adjustment.HasHueSaturation)      adj += $" hue2({layer.Adjustment.Hue},{layer.Adjustment.Saturation},{layer.Adjustment.Lightness})";
            if (layer.Adjustment.HasSolidColor)         adj += $" SoCo({layer.Adjustment.SolidColor})";
            if (layer.Adjustment.HasInvert)             adj += " Invert";
            if (layer.Adjustment.HasThreshold)          adj += $" Threshold({layer.Adjustment.ThresholdLevel})";
            if (layer.Adjustment.HasPosterize)          adj += $" Posterize({layer.Adjustment.PosterizeLevels})";
            if (layer.Adjustment.HasLevels)             adj += $" Levels({layer.Adjustment.LevelsInputBlack},{layer.Adjustment.LevelsInputWhite},{layer.Adjustment.LevelsGamma},{layer.Adjustment.LevelsOutputBlack},{layer.Adjustment.LevelsOutputWhite})";
            if (layer.Adjustment.HasCurves)             adj += $" Curves({layer.Adjustment.CurvePoints?.Count ?? 0}pts)";
            if (layer.Effects != null && layer.Effects.HasColorOverlay) adj += " ColorOverlay";

            vlog.AppendLine(
                $"  [{index}] '{layer.Name}' sect={layer.SectionType} " +
                $"rect=({layer.Left},{layer.Top},{layer.Right},{layer.Bottom}) " +
                $"blend={layer.BlendKeyRaw}({layer.BlendMode}) opacity={layer.Opacity} " +
                $"visible={layer.IsVisible} clip={layer.IsClipping} mask={mask}{adj}");
        }
    }
}
