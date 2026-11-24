using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace LigPrint {
public static class DLabelLayout {
    public static LabelFile ConvertPng(string path, float width, float height) {
        path = Disk.Local(path);
        var file = new FileInfo(path);
        if (file.Length > 32L * 1024 * 1024) throw new IOException("标签 PNG 超过 32 MB。");
        byte[] bytes = File.ReadAllBytes(path);
        using (var stream = new MemoryStream(bytes))
        using (var image = Image.FromStream(stream, false, true)) {
            if (image.RawFormat.Guid != ImageFormat.Png.Guid) throw new InvalidDataException("请选择 DLabel 导出的 PNG 图像。");
            if ((long)image.Width * image.Height > 40000000) throw new InvalidDataException("PNG 超过 4000 万像素，请降低导出分辨率。");
            var doc = new LabelFile { Width = width, Height = height };
            doc.Items.Add(new Element {
                Type = Kind.图片, W = width, H = height, X = 0, Y = 0,
                Locked = true, Text = "", ImageBytes = bytes
            });
            doc.Validate();
            return doc;
        }
    }

    public static LabelFile Import(IWin32Window owner, string path) {
        bool ddl = String.Equals(Path.GetExtension(path), ".ddl", StringComparison.OrdinalIgnoreCase);
        if (ddl) {
            string message = "请在 DLabel 中打开「" + Path.GetFileName(path) +
                "」，记下标签宽高，将整张标签另存为 PNG。需要重新绑定的文本和条码，请先在模板副本上清空。\n\n" +
                "点“确定”选择导出的 PNG。LigPrint 将固定版式作为底图导入，可在底图上添加 Excel 字段。";
            if (MessageBox.Show(owner, message, "DLabel 模板转换", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return null;
            path = LocalPicker.Pick(owner, "选择这张标签导出的 PNG", new[] { ".png" });
            if (path == null) return null;
        }
        using (var prompt = new Prompt("导入 DLabel 版式 · 填写原标签尺寸", 485, 185)) {
            prompt.Add("版式底图", new Label { Text = Path.GetFileName(path), AutoEllipsis = true });
            var width = prompt.Number("原宽度（mm）", 60, 5, 500, 2);
            var height = prompt.Number("原高度（mm）", 40, 5, 500, 2);
            if (prompt.ShowDialog(owner) != DialogResult.OK) return null;
            var doc = ConvertPng(path, (float)width.Value, (float)height.Value);
            using (var stream = new MemoryStream(doc.Items[0].ImageBytes))
            using (var image = Image.FromStream(stream)) {
                double ratio = ((double)image.Width / image.Height) / (doc.Width / doc.Height);
                if (Math.Abs(ratio - 1) > .025 &&
                    MessageBox.Show(owner, "图片宽高比与标签尺寸不一致，铺满后会拉伸图案。继续导入？",
                        "核对标签尺寸", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes) return null;
            }
            return doc;
        }
    }
}

}
