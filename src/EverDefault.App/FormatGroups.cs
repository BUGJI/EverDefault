using System.Collections.Generic;
using System.Linq;

namespace EverDefault.App
{
    /// <summary>A named bundle of common file extensions for quick rule authoring.</summary>
    public sealed class FormatGroupPreset
    {
        public FormatGroupPreset(string name, params string[] extensions)
        {
            Name = name;
            Extensions = new List<string>(extensions);
        }

        public string Name { get; private set; }

        public List<string> Extensions { get; private set; }

        public string Summary
        {
            get { return string.Join(", ", Extensions.Take(16)) + (Extensions.Count > 16 ? " …" : string.Empty); }
        }

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>Built-in groups of common formats, shared by the rule editor.</summary>
    public static class FormatGroups
    {
        public static IList<FormatGroupPreset> All { get; } = new List<FormatGroupPreset>
        {
            new FormatGroupPreset("图片",
                ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".svg", ".heic", ".avif", ".ico"),
            new FormatGroupPreset("视频",
                ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".ts"),
            new FormatGroupPreset("音频",
                ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma", ".m4a", ".opus", ".aiff"),
            new FormatGroupPreset("文档",
                ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".rtf", ".odt", ".ods", ".odp"),
            new FormatGroupPreset("电子书",
                ".epub", ".mobi", ".azw3", ".fb2"),
            new FormatGroupPreset("压缩包",
                ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso"),
            new FormatGroupPreset("网页",
                ".html", ".htm", ".mht", ".mhtml", ".xhtml"),
            new FormatGroupPreset("字体",
                ".ttf", ".otf", ".woff", ".woff2"),
            new FormatGroupPreset("文本/代码",
                ".txt", ".md", ".log", ".ini", ".cfg", ".json", ".xml", ".yml", ".yaml",
                ".cs", ".c", ".cpp", ".h", ".hpp", ".java", ".py", ".js", ".ts", ".css")
        };
    }
}
